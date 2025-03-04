#region Using declarations
using System;
using System.ComponentModel;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using System.Windows.Media;  // Required for Brushes
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class BreakOfStructureIndicator : Indicator
    {
        // ------------------------------
        // Internal state variables
        // ------------------------------
        private string lastBOS = "none";      // "bull", "bear", or "none"
        private string candidateType = "none"; // "bull", "bear", or "none"
        private double? candidateLevel = null; // level that must be broken
        private int candidateBar = -1;         // the bar index when the candidate was formed

        // ------------------------------
        // Public output as a Series<double>
        // We'll store:
        //   +1 on bars where a "Bull BOS" event is confirmed
        //   -1 on bars where a "Bear BOS" event is confirmed
        //    0 otherwise
        // ------------------------------
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> BOSSignal
        {
            get { return Values[0]; }
        }

        [NinjaScriptProperty]
        //[Display(Name="BarsRequiredForPattern", Order=1, GroupName="Parameters",
                 //Description="Number of bars needed before the logic can run (need at least 2).")]
        public int BarsRequiredForPattern { get; set; } = 4;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name        = "BreakOfStructureIndicator";
                Description = "Detects bullish/bearish BOS events based on two-bar pattern logic.";
                Calculate   = Calculate.OnBarClose;
                IsOverlay   = false;
                IsSuspendedWhileInactive = true;
                DrawOnPricePanel = true;
                PaintPriceMarkers = true;
                BarsRequiredForPattern = 2; 

                // Create one Plot to hold the BOS signal:
                AddPlot(Brushes.Red, "BOSSignal");
            }
            else if (State == State.Configure)
            {
                // Nothing special needed
            }
            else if (State == State.DataLoaded)
            {
                // Initialize state variables as needed
                lastBOS        = "none";
                candidateType  = "none";
                candidateLevel = null;
                candidateBar   = -1;
            }
        }

        protected override void OnBarUpdate()
        {
            // If we don’t have enough bars, do nothing
            if (CurrentBar < BarsRequiredForPattern)
            {
                BOSSignal[0] = 0.0;
                return;
            }

            // We'll replicate your Python logic exactly:
            //   - We look 2 bars back and 1 bar back to see if there's a bull or bear candidate
            //   - Then we confirm or cancel that candidate on subsequent bars
            //   - If confirmed, we mark a BOS event

            // Each bar, we start by setting the default signal = 0
            double currentBOSSignal = 0.0;

            // 1) Only form a candidate if none is active
            if (candidateType == "none")
            {
                bool twoBarsAgoDown = (Close[2] < Open[2]);
                bool oneBarAgoUp    = (Close[1] > Open[1]);

                bool twoBarsAgoUp   = (Close[2] > Open[2]);
                bool oneBarAgoDown  = (Close[1] < Open[1]);

                bool bullCandidatePattern = twoBarsAgoDown && oneBarAgoUp;
                bool bearCandidatePattern = twoBarsAgoUp   && oneBarAgoDown;

                // Check lastBOS for restrictions (exactly as in Python)
                if (lastBOS == "none")
                {
                    if (bullCandidatePattern)
                    {
                        candidateType  = "bull";
                        candidateBar   = CurrentBar;
                        // "candidateLevel = min(low of bar 2, low of bar 1)"
                        double lvl2 = Math.Min(Low[2], Low[1]);
                        candidateLevel = lvl2;
                    }
                    else if (bearCandidatePattern)
                    {
                        candidateType  = "bear";
                        candidateBar   = CurrentBar;
                        // "candidateLevel = max(high of bar 2, high of bar 1)"
                        double lvl2 = Math.Max(High[2], High[1]);
                        candidateLevel = lvl2;
                    }
                }
                else if (lastBOS == "bull")
                {
                    // Only allow a bear candidate
                    if (bearCandidatePattern)
                    {
                        candidateType = "bear";
                        candidateBar  = CurrentBar;
                        double lvl2   = Math.Max(High[2], High[1]);
                        candidateLevel = lvl2;
                    }
                }
                else if (lastBOS == "bear")
                {
                    // Only allow a bull candidate
                    if (bullCandidatePattern)
                    {
                        candidateType  = "bull";
                        candidateBar   = CurrentBar;
                        double lvl2    = Math.Min(Low[2], Low[1]);
                        candidateLevel = lvl2;
                    }
                }
            }

            // 2) If a candidate is active and we are past the candidate formation bar,
            //    check for confirmation or cancellation
            if (candidateType != "none" && CurrentBar > candidateBar)
            {
                // The candle’s body is Open[0] to Close[0]
                double open_val  = Open[0];
                double close_val = Close[0];

                bool bullBOSBreak = false;
                bool bearBOSBreak = false;

                // For a bullish candidate, confirm BOS if the candle’s body is entirely
                // below candidateLevel.  If the candle’s body is entirely above, we cancel.
                if (candidateType == "bull" && candidateLevel.HasValue)
                {
                    if (open_val < candidateLevel && close_val < candidateLevel)
                    {
                        bullBOSBreak = true;
                    }
                    else if (open_val > candidateLevel && close_val > candidateLevel)
                    {
                        // Cancel
                        candidateType  = "none";
                        candidateLevel = null;
                        candidateBar   = -1;
                    }
                }
                // For a bearish candidate, confirm BOS if the candle’s body is entirely
                // above candidateLevel.  If the candle’s body is entirely below, we cancel.
                else if (candidateType == "bear" && candidateLevel.HasValue)
                {
                    if (open_val > candidateLevel && close_val > candidateLevel)
                    {
                        bearBOSBreak = true;
                    }
                    else if (open_val < candidateLevel && close_val < candidateLevel)
                    {
                        // Cancel
                        candidateType  = "none";
                        candidateLevel = null;
                        candidateBar   = -1;
                    }
                }

                // If a BOS break is triggered, record the event in the Series
                if (bullBOSBreak)
                {
                    // The python logic says: 
                    //   "For a bullish candidate, the confirmed BOS is labeled as 'Bear BOS'."
                    // so let's store -1 here to reflect a "Bear BOS" event
                    currentBOSSignal = -1.0;

                    // Then we do lastBOS = "bull"
                    lastBOS = "bull";
                    Draw.ArrowDown(this, "BearBOS" + CurrentBar, false, 0, High[0] + TickSize * 2, Brushes.Red);
                    // Reset the candidate
                    candidateType  = "none";
                    candidateLevel = null;
                    candidateBar   = -1;
                }
                else if (bearBOSBreak)
                {
                    // The python logic says:
                    //   "For a bearish candidate, the confirmed BOS is labeled as 'Bull BOS'."
                    // so let's store +1 to reflect "Bull BOS"
                    currentBOSSignal = +1.0;

                    // Then we do lastBOS = "bear"
                    lastBOS = "bear";
                    Draw.ArrowUp(this, "BullBOS" + CurrentBar, false, 0, Low[0] - TickSize * 2, Brushes.Green);
                    // Reset the candidate
                    candidateType  = "none";
                    candidateLevel = null;
                    candidateBar   = -1;
                }
            }

            // Store the final signal for this bar
            BOSSignal[0] = currentBOSSignal;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private BreakOfStructureIndicator[] cacheBreakOfStructureIndicator;
		public BreakOfStructureIndicator BreakOfStructureIndicator(int barsRequiredForPattern)
		{
			return BreakOfStructureIndicator(Input, barsRequiredForPattern);
		}

		public BreakOfStructureIndicator BreakOfStructureIndicator(ISeries<double> input, int barsRequiredForPattern)
		{
			if (cacheBreakOfStructureIndicator != null)
				for (int idx = 0; idx < cacheBreakOfStructureIndicator.Length; idx++)
					if (cacheBreakOfStructureIndicator[idx] != null && cacheBreakOfStructureIndicator[idx].BarsRequiredForPattern == barsRequiredForPattern && cacheBreakOfStructureIndicator[idx].EqualsInput(input))
						return cacheBreakOfStructureIndicator[idx];
			return CacheIndicator<BreakOfStructureIndicator>(new BreakOfStructureIndicator(){ BarsRequiredForPattern = barsRequiredForPattern }, input, ref cacheBreakOfStructureIndicator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.BreakOfStructureIndicator BreakOfStructureIndicator(int barsRequiredForPattern)
		{
			return indicator.BreakOfStructureIndicator(Input, barsRequiredForPattern);
		}

		public Indicators.BreakOfStructureIndicator BreakOfStructureIndicator(ISeries<double> input , int barsRequiredForPattern)
		{
			return indicator.BreakOfStructureIndicator(input, barsRequiredForPattern);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.BreakOfStructureIndicator BreakOfStructureIndicator(int barsRequiredForPattern)
		{
			return indicator.BreakOfStructureIndicator(Input, barsRequiredForPattern);
		}

		public Indicators.BreakOfStructureIndicator BreakOfStructureIndicator(ISeries<double> input , int barsRequiredForPattern)
		{
			return indicator.BreakOfStructureIndicator(input, barsRequiredForPattern);
		}
	}
}

#endregion
