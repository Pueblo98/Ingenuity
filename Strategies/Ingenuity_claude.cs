#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.SuperDom;
using NinjaTrader.Gui.Tools;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.DrawingTools;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class IngenuityStrategyClaude : Strategy
    {
        #region Variables
        private int lookbackPeriod = 20;       // Number of bars to look back for identifying swing points
        private double atrMultiplier = 1.5;    // ATR multiplier for distance calculations
        private double riskPercentage = 1.0;   // Risk percentage per trade
        private double takeProfit1Percentage = 100; // First take profit as percentage of risk
        private double takeProfit2Percentage = 200; // Second take profit as percentage of risk
        private bool useHigherTimeframeLiquidity = true;
        private bool useEQConfluence = true;
        private bool useFVGConfirmation = true;
        private bool useOrderBlockEntry = true;

        // Market structure variables
        private List<SwingPoint> swingHighs = new List<SwingPoint>();
        private List<SwingPoint> swingLows = new List<SwingPoint>();
        private List<OrderBlock> bullishOrderBlocks = new List<OrderBlock>();
        private List<OrderBlock> bearishOrderBlocks = new List<OrderBlock>();
        private List<FairValueGap> fairValueGaps = new List<FairValueGap>();
        private List<LiquiditySweep> liquiditySweeps = new List<LiquiditySweep>();
        private List<BreakOfStructure> structureBreaks = new List<BreakOfStructure>();
        private double equilibriumLevel = 0;

        private ATR atr;
        private bool inLongPosition = false;
        private bool inShortPosition = false;
        private double entryPrice = 0;
        private double stopLossPrice = 0;
        private double takeProfit1 = 0;
        private double takeProfit2 = 0;
        private int barsSinceEntry = 0;
        private int currentPositionQuantity = 0;
        private bool isBullishBOS = false;
        private bool isBearishBOS = false;
        private bool recentLiquiditySweepBull = false;
        private bool recentLiquiditySweepBear = false;
        #endregion

        #region Classes to represent key concepts
        private class SwingPoint
        {
            public double Price { get; set; }
            public int BarIndex { get; set; }
            public bool IsHigh { get; set; }
            public bool Swept { get; set; }

            public SwingPoint(double price, int barIndex, bool isHigh)
            {
                Price = price;
                BarIndex = barIndex;
                IsHigh = isHigh;
                Swept = false;
            }
        }

        private class OrderBlock
        {
            public double High { get; set; }
            public double Low { get; set; }
            public double Open { get; set; }
            public double Close { get; set; }
            public double MidPoint { get; set; }
            public int BarIndex { get; set; }
            public bool IsBullish { get; set; }
            public bool Touched { get; set; }

            public OrderBlock(double high, double low, double open, double close, int barIndex, bool isBullish)
            {
                High = high;
                Low = low;
                Open = open;
                Close = close;
                MidPoint = (high + low) / 2;
                BarIndex = barIndex;
                IsBullish = isBullish;
                Touched = false;
            }
        }

        private class FairValueGap
        {
            public double Upper { get; set; }
            public double Lower { get; set; }
            public int BarIndex { get; set; }
            public bool IsBullish { get; set; }
            public bool Filled { get; set; }

            public FairValueGap(double upper, double lower, int barIndex, bool isBullish)
            {
                Upper = upper;
                Lower = lower;
                BarIndex = barIndex;
                IsBullish = isBullish;
                Filled = false;
            }
        }

        private class LiquiditySweep
        {
            public double SweepLevel { get; set; }
            public int BarIndex { get; set; }
            public bool IsBullish { get; set; }
            public double StopLossLevel { get; set; }

            public LiquiditySweep(double sweepLevel, int barIndex, bool isBullish, double stopLossLevel)
            {
                SweepLevel = sweepLevel;
                BarIndex = barIndex;
                IsBullish = isBullish;
                StopLossLevel = stopLossLevel;
            }
        }

        private class BreakOfStructure
        {
            public double BreakLevel { get; set; }
            public int BarIndex { get; set; }
            public bool IsBullish { get; set; }

            public BreakOfStructure(double breakLevel, int barIndex, bool isBullish)
            {
                BreakLevel = breakLevel;
                BarIndex = barIndex;
                IsBullish = isBullish;
            }
        }
        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description = @"Ingenuity Trading Strategy based on liquidity, BOS, OB, and FVG";
                Name = "IngenuityStrategy";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                ExitOnSessionCloseSeconds = 30;
                IsFillLimitOnTouch = false;
                MaximumBarsLookBack = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution = OrderFillResolution.Standard;
                Slippage = 0;
                StartBehavior = StartBehavior.WaitUntilFlat;
                TimeInForce = TimeInForce.Gtc;
                TraceOrders = false;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 20;
                // Disable this property for performance gains in Strategy Analyzer optimizations
                IsInstantiatedOnEachOptimizationIteration = true;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(Data.BarsPeriodType.Minute, 15); // Add higher timeframe
            }
            else if (State == State.DataLoaded)
            {
                atr = ATR(14);
            }
        }

        protected override void OnBarUpdate()
        {
            // Wait for required bars
            if (CurrentBar < BarsRequiredToTrade)
                return;

            // Only process on primary timeframe
            if (BarsInProgress != 0)
                return;

            // Update ATR indicator
            double currentAtr = atr[0];

            // 1. Update market structure
            UpdateSwingPoints();
            CheckLiquiditySweeps(currentAtr);
            CheckBreakOfStructure();
            IdentifyOrderBlocks();
            IdentifyFairValueGaps();
            UpdateEquilibriumLevel();

            // 2. Process entries and exits if we're in a position
            if (inLongPosition || inShortPosition)
            {
                barsSinceEntry++;
                ManageExistingPositions();
            }
            // 3. Look for new entry opportunities
            else
            {
                EvaluateEntrySetups(currentAtr);
            }

            // 4. Draw indicators for visual reference
            if (IsFirstTickOfBar)
            {
                DrawMarketStructure();
            }
        }

        #region Market Structure Analysis Methods
        private void UpdateSwingPoints()
        {
            // Simple swing point identification using pivot high/low
            // A swing high is formed when there are lower highs on both sides
            // A swing low is formed when there are higher lows on both sides
            int swingLookback = Math.Min(5, lookbackPeriod / 4);

            // Check for swing high
            bool isSwingHigh = true;
            for (int i = 1; i <= swingLookback; i++)
            {
                if (High[swingLookback] < High[swingLookback - i] || High[swingLookback] < High[swingLookback + i])
                {
                    isSwingHigh = false;
                    break;
                }
            }

            if (isSwingHigh)
            {
                swingHighs.Add(new SwingPoint(High[swingLookback], CurrentBar - swingLookback, true));
                // Clean up old swing points
                if (swingHighs.Count > 10)
                    swingHighs.RemoveAt(0);
            }

            // Check for swing low
            bool isSwingLow = true;
            for (int i = 1; i <= swingLookback; i++)
            {
                if (Low[swingLookback] > Low[swingLookback - i] || Low[swingLookback] > Low[swingLookback + i])
                {
                    isSwingLow = false;
                    break;
                }
            }

            if (isSwingLow)
            {
                swingLows.Add(new SwingPoint(Low[swingLookback], CurrentBar - swingLookback, false));
                // Clean up old swing points
                if (swingLows.Count > 10)
                    swingLows.RemoveAt(0);
            }
        }

        private void CheckLiquiditySweeps(double currentAtr)
        {
            // Check for bullish liquidity sweep (price sweeps below recent swing low)
            foreach (SwingPoint low in swingLows.Where(l => !l.Swept && CurrentBar - l.BarIndex < lookbackPeriod))
            {
                // Price went below swing low
                if (Low[0] < low.Price && Close[0] > low.Price)
                {
                    // Confirm it's a sweep with a rejection (close back above)
                    low.Swept = true;
                    double sweepSize = Math.Abs(Low[0] - low.Price);
                    
                    // Only consider valid sweeps (not too large)
                    if (sweepSize < currentAtr * 1.5)
                    {
                        double stopLossLevel = Low[0] - (0.2 * currentAtr); // Small buffer below sweep
                        liquiditySweeps.Add(new LiquiditySweep(low.Price, CurrentBar, true, stopLossLevel));
                        recentLiquiditySweepBull = true;
                        
                        Draw.Text(this, "BullSweep" + CurrentBar, "Bull Sweep", 0, Low[0], -15);
                    }
                }
            }

            // Check for bearish liquidity sweep (price sweeps above recent swing high)
            foreach (SwingPoint high in swingHighs.Where(h => !h.Swept && CurrentBar - h.BarIndex < lookbackPeriod))
            {
                // Price went above swing high
                if (High[0] > high.Price && Close[0] < high.Price)
                {
                    // Confirm it's a sweep with a rejection (close back below)
                    high.Swept = true;
                    double sweepSize = Math.Abs(High[0] - high.Price);
                    
                    // Only consider valid sweeps (not too large)
                    if (sweepSize < currentAtr * 1.5)
                    {
                        double stopLossLevel = High[0] + (0.2 * currentAtr); // Small buffer above sweep
                        liquiditySweeps.Add(new LiquiditySweep(high.Price, CurrentBar, false, stopLossLevel));
                        recentLiquiditySweepBear = true;
                        
                        Draw.Text(this, "BearSweep" + CurrentBar, "Bear Sweep", 0, High[0], 15);
                    }
                }
            }

            // Clean up old liquidity sweeps
            for (int i = liquiditySweeps.Count - 1; i >= 0; i--)
            {
                if (CurrentBar - liquiditySweeps[i].BarIndex > lookbackPeriod)
                {
                    liquiditySweeps.RemoveAt(i);
                }
            }

            // Reset recent sweep flags if they're too old
            if (recentLiquiditySweepBull && liquiditySweeps.LastOrDefault(x => x.IsBullish) != null &&
                CurrentBar - liquiditySweeps.LastOrDefault(x => x.IsBullish).BarIndex > 5)
            {
                recentLiquiditySweepBull = false;
            }

            if (recentLiquiditySweepBear && liquiditySweeps.LastOrDefault(x => !x.IsBullish) != null &&
                CurrentBar - liquiditySweeps.LastOrDefault(x => !x.IsBullish).BarIndex > 5)
            {
                recentLiquiditySweepBear = false;
            }
        }

        private void CheckBreakOfStructure()
        {
            // Find the most recent higher low and lower high
            SwingPoint recentHigherLow = null;
            SwingPoint recentLowerHigh = null;
            
            // Find the most recent higher low (for bearish BOS check)
            for (int i = swingLows.Count - 1; i >= 1; i--)
            {
                if (swingLows[i].Price > swingLows[i - 1].Price && 
                    CurrentBar - swingLows[i].BarIndex < lookbackPeriod)
                {
                    recentHigherLow = swingLows[i];
                    break;
                }
            }

            // Find the most recent lower high (for bullish BOS check)
            for (int i = swingHighs.Count - 1; i >= 1; i--)
            {
                if (swingHighs[i].Price < swingHighs[i - 1].Price &&
                    CurrentBar - swingHighs[i].BarIndex < lookbackPeriod)
                {
                    recentLowerHigh = swingHighs[i];
                    break;
                }
            }

            // Check for bullish break of structure (price closes above recent lower high)
            if (recentLowerHigh != null && Close[0] > recentLowerHigh.Price && !isBullishBOS)
            {
                structureBreaks.Add(new BreakOfStructure(recentLowerHigh.Price, CurrentBar, true));
                isBullishBOS = true;
                isBearishBOS = false;
                
                Draw.Text(this, "BullBOS" + CurrentBar, "Bull BOS", 0, Close[0], 15);
            }
            
            // Check for bearish break of structure (price closes below recent higher low)
            if (recentHigherLow != null && Close[0] < recentHigherLow.Price && !isBearishBOS)
            {
                structureBreaks.Add(new BreakOfStructure(recentHigherLow.Price, CurrentBar, false));
                isBearishBOS = true;
                isBullishBOS = false;
                
                Draw.Text(this, "BearBOS" + CurrentBar, "Bear BOS", 0, Close[0], -15);
            }

            // Clean up old structure breaks
            for (int i = structureBreaks.Count - 1; i >= 0; i--)
            {
                if (CurrentBar - structureBreaks[i].BarIndex > lookbackPeriod)
                {
                    structureBreaks.RemoveAt(i);
                }
            }

            // Reset BOS flags if they're too old
            if (isBullishBOS && structureBreaks.LastOrDefault(x => x.IsBullish) != null &&
                CurrentBar - structureBreaks.LastOrDefault(x => x.IsBullish).BarIndex > lookbackPeriod)
            {
                isBullishBOS = false;
            }

            if (isBearishBOS && structureBreaks.LastOrDefault(x => !x.IsBullish) != null &&
                CurrentBar - structureBreaks.LastOrDefault(x => !x.IsBullish).BarIndex > lookbackPeriod)
            {
                isBearishBOS = false;
            }
        }

        private void IdentifyOrderBlocks()
        {
            // For a bullish OB, we look for the last down candle before a bullish BOS
            // For a bearish OB, we look for the last up candle before a bearish BOS
            
            // Check for new bullish OB after a bearish swing
            if (structureBreaks.Count > 0 && structureBreaks.Last().IsBullish && 
                CurrentBar - structureBreaks.Last().BarIndex <= 2) // Recent bullish BOS
            {
                // Find the last bearish candle before this BOS
                for (int i = 1; i < 5; i++)
                {
                    int checkBar = structureBreaks.Last().BarIndex - i;
                    if (checkBar >= 0 && Close[CurrentBar - checkBar] < Open[CurrentBar - checkBar])
                    {
                        // Found a bearish candle - this is our bullish order block
                        OrderBlock newOB = new OrderBlock(
                            High[CurrentBar - checkBar],
                            Low[CurrentBar - checkBar],
                            Open[CurrentBar - checkBar],
                            Close[CurrentBar - checkBar],
                            checkBar,
                            true
                        );
                        bullishOrderBlocks.Add(newOB);
                        
                        Draw.Rectangle(this, "BullOB" + checkBar, false, CurrentBar - checkBar, High[CurrentBar - checkBar], 
                                      CurrentBar - checkBar, Low[CurrentBar - checkBar], Brushes.Green, Brushes.Green, 60);
                        
                        break;
                    }
                }
            }
            
            // Check for new bearish OB after a bullish swing
            if (structureBreaks.Count > 0 && !structureBreaks.Last().IsBullish && 
                CurrentBar - structureBreaks.Last().BarIndex <= 2) // Recent bearish BOS
            {
                // Find the last bullish candle before this BOS
                for (int i = 1; i < 5; i++)
                {
                    int checkBar = structureBreaks.Last().BarIndex - i;
                    if (checkBar >= 0 && Close[CurrentBar - checkBar] > Open[CurrentBar - checkBar])
                    {
                        // Found a bullish candle - this is our bearish order block
                        OrderBlock newOB = new OrderBlock(
                            High[CurrentBar - checkBar],
                            Low[CurrentBar - checkBar],
                            Open[CurrentBar - checkBar],
                            Close[CurrentBar - checkBar],
                            checkBar,
                            false
                        );
                        bearishOrderBlocks.Add(newOB);
                        
                        Draw.Rectangle(this, "BearOB" + checkBar, false, CurrentBar - checkBar, High[CurrentBar - checkBar], 
                                      CurrentBar - checkBar, Low[CurrentBar - checkBar], Brushes.Red, Brushes.Red, 60);
                        
                        break;
                    }
                }
            }

            // Check if price has touched any order blocks
            foreach (OrderBlock ob in bullishOrderBlocks.Where(o => !o.Touched))
            {
                if (Low[0] <= ob.High && Low[0] >= ob.Low)
                {
                    ob.Touched = true;
                    Draw.Diamond(this, "BullOBTouch" + CurrentBar, false, 0, Low[0], Brushes.Green);
                }
            }

            foreach (OrderBlock ob in bearishOrderBlocks.Where(o => !o.Touched))
            {
                if (High[0] >= ob.Low && High[0] <= ob.High)
                {
                    ob.Touched = true;
                    Draw.Diamond(this, "BearOBTouch" + CurrentBar, false, 0, High[0], Brushes.Red);
                }
            }

            // Clean up old order blocks
            bullishOrderBlocks.RemoveAll(ob => CurrentBar - ob.BarIndex > lookbackPeriod);
            bearishOrderBlocks.RemoveAll(ob => CurrentBar - ob.BarIndex > lookbackPeriod);
        }

        private void IdentifyFairValueGaps()
        {
            // Check for bullish FVG (low of current bar is above high of previous bar)
            if (Low[1] > High[2])
            {
                FairValueGap newFVG = new FairValueGap(Low[1], High[2], CurrentBar - 1, true);
                fairValueGaps.Add(newFVG);
                
                Draw.Rectangle(this, "BullFVG" + (CurrentBar-1), false, CurrentBar - 1, Low[1], 
                              CurrentBar - 1, High[2], Brushes.LimeGreen, Brushes.LimeGreen, 30);
            }
            
            // Check for bearish FVG (high of current bar is below low of previous bar)
            if (High[1] < Low[2])
            {
                FairValueGap newFVG = new FairValueGap(Low[2], High[1], CurrentBar - 1, false);
                fairValueGaps.Add(newFVG);
                
                Draw.Rectangle(this, "BearFVG" + (CurrentBar-1), false, CurrentBar - 1, Low[2], 
                              CurrentBar - 1, High[1], Brushes.Crimson, Brushes.Crimson, 30);
            }
            // Check if price has filled any fair value gaps
            foreach (FairValueGap fvg in fairValueGaps.Where(f => !f.Filled))
            {
                if (fvg.IsBullish && Low[0] <= fvg.Upper && Low[0] >= fvg.Lower)
                {
                    fvg.Filled = true;
                    Draw.Diamond(this, "BullFVGFill" + CurrentBar, false, 0, Low[0], Brushes.Green);
                }
                else if (!fvg.IsBullish && High[0] >= fvg.Lower && High[0] <= fvg.Upper)
                {
                    fvg.Filled = true;
                    Draw.Diamond(this, "BearFVGFill" + CurrentBar, false, 0, High[0], Brushes.Red);
                }
            }

            // Clean up old FVGs
            fairValueGaps.RemoveAll(fvg => CurrentBar - fvg.BarIndex > lookbackPeriod);
        }

        private void UpdateEquilibriumLevel()
        {
            // Calculate equilibrium level for recent swing (50% retracement level)
            if (swingHighs.Count > 0 && swingLows.Count > 0)
            {
                SwingPoint recentHigh = swingHighs.Last();
                SwingPoint recentLow = swingLows.Last();
                
                // Make sure we're using the most recent ones
                if (recentHigh.BarIndex > recentLow.BarIndex)
                {
                    // Latest swing is a high, so look for the most recent low
                    for (int i = swingLows.Count - 1; i >= 0; i--)
                    {
                        if (swingLows[i].BarIndex < recentHigh.BarIndex)
                        {
                            recentLow = swingLows[i];
                            break;
                        }
                    }
                }
                else
                {
                    // Latest swing is a low, so look for the most recent high
                    for (int i = swingHighs.Count - 1; i >= 0; i--)
                    {
                        if (swingHighs[i].BarIndex < recentLow.BarIndex)
                        {
                            recentHigh = swingHighs[i];
                            break;
                        }
                    }
                }
                
                // Calculate 50% level
                equilibriumLevel = (recentHigh.Price + recentLow.Price) / 2;
                
                // Draw EQ line
                Draw.Line(this, "EQ" + CurrentBar, false, 10, equilibriumLevel, 0, equilibriumLevel, Brushes.Yellow, DashStyleHelper.Dash, 1);
            }
        }
        #endregion

        #region Trade Management Methods
        private void EvaluateEntrySetups(double currentAtr)
        {
            // 1. LIQ Sweep + BOS Setup
            if (Position.MarketPosition == MarketPosition.Flat)
            {
                // Check for bullish setup (bearish sweep + bullish BOS)
                if (recentLiquiditySweepBull && isBullishBOS)
                {
                    LiquiditySweep bullSweep = liquiditySweeps.LastOrDefault(s => s.IsBullish);
                    BreakOfStructure bullBOS = structureBreaks.LastOrDefault(b => b.IsBullish);
                    
                    if (bullSweep != null && bullBOS != null && 
                        bullBOS.BarIndex > bullSweep.BarIndex && 
                        CurrentBar - bullBOS.BarIndex <= 3)
                    {
                        // Check for additional confirmation - OB touch or FVG fill
                        bool confirmationFound = false;
                        
                        if (useOrderBlockEntry)
                        {
                            // Check for recent bullish OB touch
                            OrderBlock recentBullOB = bullishOrderBlocks.LastOrDefault(ob => ob.Touched);
                            if (recentBullOB != null && recentBullOB.Touched && 
                                recentBullOB.BarIndex < bullBOS.BarIndex)
                            {
                                confirmationFound = true;
                            }
                        }
                        
                        if (useFVGConfirmation && !confirmationFound)
                        {
                            // Check for recent bullish FVG fill
                            FairValueGap recentBullFVG = fairValueGaps.LastOrDefault(fvg => 
                                fvg.IsBullish && fvg.Filled && fvg.BarIndex < bullBOS.BarIndex);
                            
                            if (recentBullFVG != null)
                            {
                                confirmationFound = true;
                            }
                        }
                        
                        // If no OB/FVG confirmation required or found
                        if (!useOrderBlockEntry && !useFVGConfirmation || confirmationFound)
                        {
                            // Check if price is at a good level (below EQ for bullish)
                            bool priceDiscounted = useEQConfluence ? (Close[0] <= equilibriumLevel) : true;
                            
                            if (priceDiscounted)
                            {
                                // Entry calculations
                                stopLossPrice = bullSweep.StopLossLevel;
                                entryPrice = Close[0];
                                double riskPerShare = Math.Abs(entryPrice - stopLossPrice);
                                
                                // Position sizing based on risk percentage
                                double accountValue = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                                double riskAmount = accountValue * (riskPercentage / 100.0);
                                int quantity = (int)Math.Floor(riskAmount / riskPerShare / SymbolInfo.PointValue);
                                
                                // Set take profit levels
                                takeProfit1 = entryPrice + (riskPerShare * (takeProfit1Percentage / 100.0));
                                takeProfit2 = entryPrice + (riskPerShare * (takeProfit2Percentage / 100.0));
                                
                                // Execute the long entry
                                EnterLong(quantity, "BullEntry");
                                inLongPosition = true;
                                barsSinceEntry = 0;
                                currentPositionQuantity = quantity;
                                
                                Print("LONG Entry: Price=" + entryPrice + ", SL=" + stopLossPrice + 
                                      ", TP1=" + takeProfit1 + ", TP2=" + takeProfit2 + ", Qty=" + quantity);
                            }
                        }
                    }
                }
                
                // Check for bearish setup (bullish sweep + bearish BOS)
                if (recentLiquiditySweepBear && isBearishBOS)
                {
                    LiquiditySweep bearSweep = liquiditySweeps.LastOrDefault(s => !s.IsBullish);
                    BreakOfStructure bearBOS = structureBreaks.LastOrDefault(b => !b.IsBullish);
                    
                    if (bearSweep != null && bearBOS != null && 
                        bearBOS.BarIndex > bearSweep.BarIndex && 
                        CurrentBar - bearBOS.BarIndex <= 3)
                    {
                        // Check for additional confirmation - OB touch or FVG fill
                        bool confirmationFound = false;
                        
                        if (useOrderBlockEntry)
                        {
                            // Check for recent bearish OB touch
                            OrderBlock recentBearOB = bearishOrderBlocks.LastOrDefault(ob => ob.Touched);
                            if (recentBearOB != null && recentBearOB.Touched && 
                                recentBearOB.BarIndex < bearBOS.BarIndex)
                            {
                                confirmationFound = true;
                            }
                        }
                        
                        if (useFVGConfirmation && !confirmationFound)
                        {
                            // Check for recent bearish FVG fill
                            FairValueGap recentBearFVG = fairValueGaps.LastOrDefault(fvg => 
                                !fvg.IsBullish && fvg.Filled && fvg.BarIndex < bearBOS.BarIndex);
                            
                            if (recentBearFVG != null)
                            {
                                confirmationFound = true;
                            }
                        }
                        
                        // If no OB/FVG confirmation required or found
                        if (!useOrderBlockEntry && !useFVGConfirmation || confirmationFound)
                        {
                            // Check if price is at a good level (above EQ for bearish)
                            bool pricePremium = useEQConfluence ? (Close[0] >= equilibriumLevel) : true;
                            
                            if (pricePremium)
                            {
                                // Entry calculations
                                stopLossPrice = bearSweep.StopLossLevel;
                                entryPrice = Close[0];
                                double riskPerShare = Math.Abs(entryPrice - stopLossPrice);
                                
                                // Position sizing based on risk percentage
                                double accountValue = Account.Get(AccountItem.CashValue, Currency.UsDollar);
                                double riskAmount = accountValue * (riskPercentage / 100.0);
                                int quantity = (int)Math.Floor(riskAmount / riskPerShare / SymbolInfo.PointValue);
                                
                                // Set take profit levels
                                takeProfit1 = entryPrice - (riskPerShare * (takeProfit1Percentage / 100.0));
                                takeProfit2 = entryPrice - (riskPerShare * (takeProfit2Percentage / 100.0));
                                
                                // Execute the short entry
                                EnterShort(quantity, "BearEntry");
                                inShortPosition = true;
                                barsSinceEntry = 0;
                                currentPositionQuantity = quantity;
                                
                                Print("SHORT Entry: Price=" + entryPrice + ", SL=" + stopLossPrice + 
                                      ", TP1=" + takeProfit1 + ", TP2=" + takeProfit2 + ", Qty=" + quantity);
                            }
                        }
                    }
                }
            }
        }

        private void ManageExistingPositions()
        {
            // Check for stop loss hit
            if (inLongPosition)
            {
                // For long positions, check if low price touched or crossed stop loss
                if (Low[0] <= stopLossPrice)
                {
                    ExitLong(currentPositionQuantity, "StopLoss", stopLossPrice);
                    Print("LONG Exit (Stop Loss): Price=" + stopLossPrice);
                    ResetPositionState();
                    return;
                }
                
                // Check for take profit 1 (exit partial position)
                if (High[0] >= takeProfit1 && currentPositionQuantity > 1)
                {
                    int tpQuantity = currentPositionQuantity / 2;
                    ExitLong(tpQuantity, "TP1", takeProfit1);
                    currentPositionQuantity -= tpQuantity;
                    Print("LONG Partial Exit (TP1): Price=" + takeProfit1 + ", Qty=" + tpQuantity);
                    
                    // Move stop loss to break even for remaining position
                    stopLossPrice = entryPrice;
                }
                
                // Check for take profit 2 (exit remaining position)
                if (High[0] >= takeProfit2 && currentPositionQuantity > 0)
                {
                    ExitLong(currentPositionQuantity, "TP2", takeProfit2);
                    Print("LONG Final Exit (TP2): Price=" + takeProfit2 + ", Qty=" + currentPositionQuantity);
                    ResetPositionState();
                }
            }
            else if (inShortPosition)
            {
                // For short positions, check if high price touched or crossed stop loss
                if (High[0] >= stopLossPrice)
                {
                    ExitShort(currentPositionQuantity, "StopLoss", stopLossPrice);
                    Print("SHORT Exit (Stop Loss): Price=" + stopLossPrice);
                    ResetPositionState();
                    return;
                }
                
                // Check for take profit 1 (exit partial position)
                if (Low[0] <= takeProfit1 && currentPositionQuantity > 1)
                {
                    int tpQuantity = currentPositionQuantity / 2;
                    ExitShort(tpQuantity, "TP1", takeProfit1);
                    currentPositionQuantity -= tpQuantity;
                    Print("SHORT Partial Exit (TP1): Price=" + takeProfit1 + ", Qty=" + tpQuantity);
                    
                    // Move stop loss to break even for remaining position
                    stopLossPrice = entryPrice;
                }
                
                // Check for take profit 2 (exit remaining position)
                if (Low[0] <= takeProfit2 && currentPositionQuantity > 0)
                {
                    ExitShort(currentPositionQuantity, "TP2", takeProfit2);
                    Print("SHORT Final Exit (TP2): Price=" + takeProfit2 + ", Qty=" + currentPositionQuantity);
                    ResetPositionState();
                }
            }
            
            // Time-based exit (if position has been open too long)
            if ((inLongPosition || inShortPosition) && barsSinceEntry > 20)
            {
                if (inLongPosition)
                {
                    ExitLong(currentPositionQuantity, "TimeExit", Close[0]);
                    Print("LONG Exit (Time): Price=" + Close[0]);
                }
                else
                {
                    ExitShort(currentPositionQuantity, "TimeExit", Close[0]);
                    Print("SHORT Exit (Time): Price=" + Close[0]);
                }
                
                ResetPositionState();
            }
        }
        
        private void ResetPositionState()
        {
            inLongPosition = false;
            inShortPosition = false;
            entryPrice = 0;
            stopLossPrice = 0;
            takeProfit1 = 0;
            takeProfit2 = 0;
            barsSinceEntry = 0;
            currentPositionQuantity = 0;
        }
        #endregion

        #region Visualization Methods
        private void DrawMarketStructure()
        {
            // Clear previous drawings
            ClearOutputWindow();
            
            // Draw active order blocks, FVGs, etc.
            // This is already handled in the identification methods
        }
        #endregion
        
        #region Strategy Parameters
        [NinjaScriptProperty]
        [Range(5, 50)]
        [Display(Name = "Lookback Period", Description = "Number of bars to look back for swing points", Order = 1, GroupName = "Strategy Parameters")]
        public int LookbackPeriod
        {
            get { return lookbackPeriod; }
            set { lookbackPeriod = value; }
        }
        
        [NinjaScriptProperty]
        [Range(0.5, 3.0)]
        [Display(Name = "ATR Multiplier", Description = "ATR multiplier for sweep detection", Order = 2, GroupName = "Strategy Parameters")]
        public double AtrMultiplier
        {
            get { return atrMultiplier; }
            set { atrMultiplier = value; }
        }
        
        [NinjaScriptProperty]
        [Range(0.1, 5.0)]
        [Display(Name = "Risk Percentage", Description = "Risk percentage per trade", Order = 3, GroupName = "Strategy Parameters")]
        public double RiskPercentage
        {
            get { return riskPercentage; }
            set { riskPercentage = value; }
        }
        
        [NinjaScriptProperty]
        [Range(50, 300)]
        [Display(Name = "Take Profit 1 %", Description = "First take profit as percentage of risk", Order = 4, GroupName = "Strategy Parameters")]
        public double TakeProfit1Percentage
        {
            get { return takeProfit1Percentage; }
            set { takeProfit1Percentage = value; }
        }
        
        [NinjaScriptProperty]
        [Range(100, 500)]
        [Display(Name = "Take Profit 2 %", Description = "Second take profit as percentage of risk", Order = 5, GroupName = "Strategy Parameters")]
        public double TakeProfit2Percentage
        {
            get { return takeProfit2Percentage; }
            set { takeProfit2Percentage = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Higher TF Liquidity", Description = "Consider higher timeframe liquidity levels", Order = 6, GroupName = "Strategy Parameters")]
        public bool UseHigherTimeframeLiquidity
        {
            get { return useHigherTimeframeLiquidity; }
            set { useHigherTimeframeLiquidity = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Use EQ Confluence", Description = "Require price to be at a discount/premium to EQ", Order = 7, GroupName = "Strategy Parameters")]
        public bool UseEQConfluence
        {
            get { return useEQConfluence; }
            set { useEQConfluence = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Use FVG Confirmation", Description = "Require FVG fill for confirmation", Order = 8, GroupName = "Strategy Parameters")]
        public bool UseFVGConfirmation
        {
            get { return useFVGConfirmation; }
            set { useFVGConfirmation = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Order Block Entry", Description = "Require order block touch for entry", Order = 9, GroupName = "Strategy Parameters")]
        public bool UseOrderBlockEntry
        {
            get { return useOrderBlockEntry; }
            set { useOrderBlockEntry = value; }
        }
        #endregion
    }
}
