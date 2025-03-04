#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using NinjaTrader.Data;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using System.Windows.Media;  // Required for Brushes
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    public class LiquiditySweepIndicator : Indicator
    {
        #region Properties
        //[NinjaScriptProperty]
        //[Range(1, int.MaxValue)]
        //[Display(Name = "SwingLength", Order = 1, GroupName = "Parameters")]
        public int SwingLength { get; set; } 

        [NinjaScriptProperty]
        //[Range(0.0, double.MaxValue)]
        //[Display(Name = "Threshold", Order = 2, GroupName = "Parameters",
                 //Description = "Min difference from neighboring bars to qualify as swing")]
        public double Threshold { get; set; }

        [NinjaScriptProperty]
        //[Range(1, 1000)]
        //[Display(Name = "ValidHours", Order = 3, GroupName = "Parameters",
                 //Description = "How many hours a swing point remains ‘active’ for potential sweep")]
        public int ValidHours { get; set; }

        // This is the Series<double> we plot for detection:
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> LiquiditySignal
        {
            get { return Values[0]; }
        }
        #endregion

        // Internal data structures to track active swing highs/lows:
        private List<SwingPoint> activeHighs; 
        private List<SwingPoint> activeLows;  

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "LiquiditySweepIndicator";
                Description = "Detect liquidity sweeps similar to the Python logic";
                Calculate = Calculate.OnBarClose;
                 // ✅ Enable Overlay on Main Chart (instead of a separate panel)
                IsOverlay = true;  // ✅ This ensures the indicator is drawn directly on the price chart
                DrawOnPricePanel = true;  // ✅ Makes sure it is drawn on the price panel
                PaintPriceMarkers = true;  // ✅ Shows the values on the y-axis

                ScaleJustification = NinjaTrader.Gui.Chart.ScaleJustification.Right;
                IsSuspendedWhileInactive = true;

                // Default parameter values
                SwingLength = 12;
                Threshold   = 10;
                ValidHours  = 24;

                // Add a single plot to store the bullish/bearish sweep signals:
                AddPlot(Brushes.Blue, "LiquiditySignal");
            }
            else if (State == State.Configure)
            {
                // Nothing special needed here
            }
            else if (State == State.DataLoaded)
            {
                activeHighs = new List<SwingPoint>();
                activeLows  = new List<SwingPoint>();
            }
        }

        protected override void OnBarUpdate()
        {
            // If not enough bars, do nothing
            if (CurrentBar < SwingLength) 
                return;

            // Each bar we’ll do:
            // 1) Remove expired swings from activeHighs/activeLows
            // 2) Check if we have a new local swing high or low at [0]
            // 3) Check for sweeps: 
            //      - Bearish sweep: if High[0] > a prior swingHigh price 
            //                       AND Close[0] < that same swingHigh price
            //      - Bullish sweep: if Low[0] < a prior swingLow price
            //                       AND Close[0] > that same swingLow price
            //    Then mark LiquiditySignal[0] = ±1

            // Initially, set signal to 0 for the bar unless we find a sweep:
            double signalValue = 0.0;  

            // (1) Prune old swings outside the validity window
            DateTime cutoffTime = Time[0].AddHours(-ValidHours);
            for (int i = activeHighs.Count - 1; i >= 0; i--)
            {
                if (activeHighs[i].Time < cutoffTime)
                    activeHighs.RemoveAt(i);
            }
            for (int i = activeLows.Count - 1; i >= 0; i--)
            {
                if (activeLows[i].Time < cutoffTime) 
                    activeLows.RemoveAt(i);
            }

            // (2) Check if the current bar is a new local swing high or low
            // We compare bar[0].High to neighbors from [1..SwingLength], similarly for Low
            // Also optionally apply a "threshold" condition if desired.

            // To do local max check, ensure High[0] >= High[i] for i in [1..SwingLength] 
            // (or strictly > for true maxima).
            bool isSwingHigh = true;
            for (int i = 1; i <= SwingLength; i++)
            {
                if (CurrentBar - i < 0) break;

                if (High[0] < High[i])
                {
                    isSwingHigh = false;
                    break;
                }
            }
            // If threshold is used to ensure the difference from neighbors is large enough:
            if (isSwingHigh && Threshold > 0.0)
            {
                // We can check the highest neighbor's High for the difference
                double neighborMax = double.MinValue;
                for (int i = 1; i <= SwingLength; i++)
                {
                    if (CurrentBar - i < 0) break;
                    if (High[i] > neighborMax)
                        neighborMax = High[i];
                }
                // if difference is < threshold, skip
                if ((High[0] - neighborMax) < Threshold)
                    isSwingHigh = false;
            }

            if (isSwingHigh)
            {
                SwingPoint sp = new SwingPoint();
                sp.Time  = Time[0];
                sp.Price = High[0];
                activeHighs.Add(sp);
            }

            // Similarly check for local swing low
            bool isSwingLow = true;
            for (int i = 1; i <= SwingLength; i++)
            {
                if (CurrentBar - i < 0) break;
                if (Low[0] > Low[i])
                {
                    isSwingLow = false;
                    break;
                }
            }
            if (isSwingLow && Threshold > 0.0)
            {
                double neighborMin = double.MaxValue;
                for (int i = 1; i <= SwingLength; i++)
                {
                    if (CurrentBar - i < 0) break;
                    if (Low[i] < neighborMin)
                        neighborMin = Low[i];
                }
                if ((neighborMin - Low[0]) < Threshold)
                    isSwingLow = false;
            }

            if (isSwingLow)
            {
                SwingPoint sp = new SwingPoint();
                sp.Time  = Time[0];
                sp.Price = Low[0];
                activeLows.Add(sp);
            }

            // (3) Check for sweeps
            //  - Bearish sweep: High[0] > swingPrice & Close[0] < swingPrice
            //  - Bullish sweep: Low[0] < swingPrice & Close[0] > swingPrice

            // We'll store indices to remove them after the loop
            List<int> removeHighs = new List<int>();
            for (int i = 0; i < activeHighs.Count; i++)
            {
                if (High[0] > activeHighs[i].Price && Close[0] < activeHighs[i].Price)
                {
                    // Bearish sweep
                    signalValue = -1.0;
                    removeHighs.Add(i);
                }
            }
            // Remove them in descending index order
            removeHighs.Reverse();
            foreach (int idx in removeHighs)
            {
                activeHighs.RemoveAt(idx);
            }

            List<int> removeLows = new List<int>();
            for (int i = 0; i < activeLows.Count; i++)
            {
                if (Low[0] < activeLows[i].Price && Close[0] > activeLows[i].Price)
                {
                    // Bullish sweep
                    signalValue = +1.0;
                    removeLows.Add(i);
                }
            }
            removeLows.Reverse();
            foreach (int idx in removeLows)
            {
                activeLows.RemoveAt(idx);
            }

            // Finally, assign the signal for this bar
            LiquiditySignal[0] = signalValue;
            if (signalValue == 1.0)  // Bullish Liquidity Sweep
            {
                Draw.ArrowUp(this, "BullishSweep" + CurrentBar, false, 0, Low[0] - TickSize * 2, Brushes.Green);
            }
            else if (signalValue == -1.0)  // Bearish Liquidity Sweep
            {
                Draw.ArrowDown(this, "BearishSweep" + CurrentBar, false, 0, High[0] + TickSize * 2, Brushes.Red);
            }
        }

        // Helper class to store a swing point
        private class SwingPoint
        {
            public DateTime Time { get; set; }
            public double   Price { get; set; }
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private LiquiditySweepIndicator[] cacheLiquiditySweepIndicator;
		public LiquiditySweepIndicator LiquiditySweepIndicator(double threshold, int validHours)
		{
			return LiquiditySweepIndicator(Input, threshold, validHours);
		}

		public LiquiditySweepIndicator LiquiditySweepIndicator(ISeries<double> input, double threshold, int validHours)
		{
			if (cacheLiquiditySweepIndicator != null)
				for (int idx = 0; idx < cacheLiquiditySweepIndicator.Length; idx++)
					if (cacheLiquiditySweepIndicator[idx] != null && cacheLiquiditySweepIndicator[idx].Threshold == threshold && cacheLiquiditySweepIndicator[idx].ValidHours == validHours && cacheLiquiditySweepIndicator[idx].EqualsInput(input))
						return cacheLiquiditySweepIndicator[idx];
			return CacheIndicator<LiquiditySweepIndicator>(new LiquiditySweepIndicator(){ Threshold = threshold, ValidHours = validHours }, input, ref cacheLiquiditySweepIndicator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.LiquiditySweepIndicator LiquiditySweepIndicator(double threshold, int validHours)
		{
			return indicator.LiquiditySweepIndicator(Input, threshold, validHours);
		}

		public Indicators.LiquiditySweepIndicator LiquiditySweepIndicator(ISeries<double> input , double threshold, int validHours)
		{
			return indicator.LiquiditySweepIndicator(input, threshold, validHours);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.LiquiditySweepIndicator LiquiditySweepIndicator(double threshold, int validHours)
		{
			return indicator.LiquiditySweepIndicator(Input, threshold, validHours);
		}

		public Indicators.LiquiditySweepIndicator LiquiditySweepIndicator(ISeries<double> input , double threshold, int validHours)
		{
			return indicator.LiquiditySweepIndicator(input, threshold, validHours);
		}
	}
}

#endregion
