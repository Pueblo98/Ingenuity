#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using System.Windows.Media;  // Required for Brushes
#endregion

namespace NinjaTrader.NinjaScript.Indicators
{
    /// <summary>
    /// Detects potential Bullish/Bearish order blocks by monitoring swing highs/lows,
    /// ATR-based thresholds, and invalidation logic (adapted from Python code).
    /// </summary>
    public class OrderBlocksIndicator : Indicator
    {
        #region Input Parameters
        // Remove or comment out the Range/Display attributes to avoid DataAnnotations issues.
        // You can keep [NinjaScriptProperty] if you want these in the UI.

        // [NinjaScriptProperty]
        public int SwingLength { get; set; } = 10;

        // [NinjaScriptProperty]
        public int MaxOrderBlocks { get; set; } = 30;

        // [NinjaScriptProperty]
        public int ATRPeriod { get; set; } = 10;

        // [NinjaScriptProperty]
        public double ATRMultiplier { get; set; } = 3.5;
        #endregion

        #region Internal classes
        private class SwingHigh
        {
            public int    BarIndex;
            public double Price;
            public bool   Broken;
        }

        private class SwingLow
        {
            public int    BarIndex;
            public double Price;
            public bool   Broken;
        }

        public class OrderBlock
        {
            public int      StartBarIndex;
            public DateTime StartTime;
            public double   Top;
            public double   Bottom;
            public string   Type;       // "Bullish" or "Bearish"
            public double   Volume;     // <--- Stored as double now
            public bool     Invalidated;
        }
        #endregion

        #region Output / Collections
        [Browsable(false)]
        [XmlIgnore]
        public List<OrderBlock> OrderBlocks
        {
            get { return orderBlocks; }
        }
        private List<OrderBlock> orderBlocks;

        // Optional numeric “signal” output
        [Browsable(false)]
        [XmlIgnore]
        public Series<double> OBSignal
        {
            get { return Values[0]; }
        }
        #endregion

        #region Private fields
        private List<SwingHigh> activeSwingHighs;
        private List<SwingLow>  activeSwingLows;

        // Built-in NinjaTrader 8 ATR
        private ATR atrIndicator;
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name                      = "OrderBlocksIndicator";
                Description               = "Detects bullish or bearish order blocks using ATR and swing points.";
                IsSuspendedWhileInactive  = true;

                // Default parameter values:
                SwingLength    = 10;
                MaxOrderBlocks = 30;
                ATRPeriod      = 10;
                ATRMultiplier  = 3.5;

                // Provide at least one plot for NinjaTrader to show
                AddPlot(System.Windows.Media.Brushes.Blue, "OBSignal");
            }
            else if (State == State.DataLoaded)
            {
                orderBlocks      = new List<OrderBlock>();
                activeSwingHighs = new List<SwingHigh>();
                activeSwingLows  = new List<SwingLow>();

                // Instantiate built-in ATR
                atrIndicator = ATR(ATRPeriod);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < ATRPeriod)
                return;

            // 1) Current ATR
            double currentATR  = atrIndicator[0];
            double currentHigh = High[0];
            double currentLow  = Low[0];
            double currentClose= Close[0];

            // 2) Check for local swing high/low in the past SwingLength bars
            if (CurrentBar >= SwingLength)
            {
                bool isSwingHigh = true;
                bool isSwingLow  = true;
                double candidateHigh = High[0];
                double candidateLow  = Low[0];

                for (int i = 1; i <= SwingLength; i++)
                {
                    if (High[i] > candidateHigh)
                        isSwingHigh = false;
                    if (Low[i] < candidateLow)
                        isSwingLow = false;
                    if (!isSwingHigh && !isSwingLow)
                        break;
                }

                if (isSwingHigh)
                {
                    activeSwingHighs.Add(new SwingHigh
                    {
                        BarIndex = CurrentBar,
                        Price    = candidateHigh,
                        Broken   = false
                    });
                }
                if (isSwingLow)
                {
                    activeSwingLows.Add(new SwingLow
                    {
                        BarIndex = CurrentBar,
                        Price    = candidateLow,
                        Broken   = false
                    });
                }
            }

            // 3) Check for Bullish OB formation
            foreach (var sh in new List<SwingHigh>(activeSwingHighs)) 
            {
                if (!sh.Broken && currentClose > sh.Price)
                {
                    sh.Broken = true;

                    // Reaction window: [sh.BarIndex+1 .. CurrentBar-1]
                    if (sh.BarIndex + 1 < CurrentBar - 1)
                    {
                        double reactionLow  = double.MaxValue;
                        double reactionHigh = 0.0;
                        double sumVolume    = 0.0;

                        for (int b = sh.BarIndex + 1; b < CurrentBar; b++)
                        {
                            if (Low[b] < reactionLow)
                            {
                                reactionLow  = Low[b];
                                reactionHigh = High[b];
                            }
                            sumVolume += Volume[b]; 
                        }

                        if ((reactionHigh - reactionLow) <= currentATR * ATRMultiplier)
                        {
                            orderBlocks.Add(new OrderBlock
                            {
                                StartBarIndex = sh.BarIndex,
                                StartTime     = Time[sh.BarIndex],
                                Top           = reactionHigh,
                                Bottom        = reactionLow,
                                Type          = "Bullish",
                                Volume        = sumVolume,
                                Invalidated   = false
                            });
                        }
                    }
                }
            }

            // 4) Check for Bearish OB formation
            foreach (var sl in new List<SwingLow>(activeSwingLows))
            {
                if (!sl.Broken && currentClose < sl.Price)
                {
                    sl.Broken = true;

                    if (sl.BarIndex + 1 < CurrentBar - 1)
                    {
                        double reactionHigh = double.MinValue;
                        double reactionLow  = double.MaxValue;
                        double sumVolume    = 0.0;

                        for (int b = sl.BarIndex + 1; b < CurrentBar; b++)
                        {
                            if (High[b] > reactionHigh)
                            {
                                reactionHigh = High[b];
                                reactionLow  = Low[b];
                            }
                            sumVolume += Volume[b];
                        }

                        if ((reactionHigh - reactionLow) <= currentATR * ATRMultiplier)
                        {
                            orderBlocks.Add(new OrderBlock
                            {
                                StartBarIndex = sl.BarIndex,
                                StartTime     = Time[sl.BarIndex],
                                Top           = reactionHigh,
                                Bottom        = reactionLow,
                                Type          = "Bearish",
                                Volume        = sumVolume,
                                Invalidated   = false
                            });
                        }
                    }
                }
            }

            // 5) Invalidation check
            foreach (var ob in new List<OrderBlock>(orderBlocks))
            {
                if (!ob.Invalidated)
                {
                    if (ob.Type == "Bullish" && currentLow < ob.Bottom)
                        ob.Invalidated = true;
                    else if (ob.Type == "Bearish" && currentHigh > ob.Top)
                        ob.Invalidated = true;
                }
            }

            // 6) Limit total stored blocks
            if (orderBlocks.Count > MaxOrderBlocks)
            {
                orderBlocks.Sort((a, b) => a.StartBarIndex.CompareTo(b.StartBarIndex));
                while (orderBlocks.Count > MaxOrderBlocks)
                    orderBlocks.RemoveAt(0);
            }

            // 7) Output a simple numeric signal: difference between active bullish & bearish
            double bullCount = 0, bearCount = 0;
            foreach (var ob in orderBlocks)
            {
                if (!ob.Invalidated)
                {
                    if (ob.Type == "Bullish") bullCount++;
                    if (ob.Type == "Bearish") bearCount++;
                }
            }
            OBSignal[0] = bullCount - bearCount;
        }
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private OrderBlocksIndicator[] cacheOrderBlocksIndicator;
		public OrderBlocksIndicator OrderBlocksIndicator()
		{
			return OrderBlocksIndicator(Input);
		}

		public OrderBlocksIndicator OrderBlocksIndicator(ISeries<double> input)
		{
			if (cacheOrderBlocksIndicator != null)
				for (int idx = 0; idx < cacheOrderBlocksIndicator.Length; idx++)
					if (cacheOrderBlocksIndicator[idx] != null &&  cacheOrderBlocksIndicator[idx].EqualsInput(input))
						return cacheOrderBlocksIndicator[idx];
			return CacheIndicator<OrderBlocksIndicator>(new OrderBlocksIndicator(), input, ref cacheOrderBlocksIndicator);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.OrderBlocksIndicator OrderBlocksIndicator()
		{
			return indicator.OrderBlocksIndicator(Input);
		}

		public Indicators.OrderBlocksIndicator OrderBlocksIndicator(ISeries<double> input )
		{
			return indicator.OrderBlocksIndicator(input);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.OrderBlocksIndicator OrderBlocksIndicator()
		{
			return indicator.OrderBlocksIndicator(Input);
		}

		public Indicators.OrderBlocksIndicator OrderBlocksIndicator(ISeries<double> input )
		{
			return indicator.OrderBlocksIndicator(input);
		}
	}
}

#endregion
