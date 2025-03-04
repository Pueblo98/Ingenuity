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

//This namespace holds Strategies in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.Strategies
{
    public class IngenuityStrategyClaude : Strategy
    {
        #region Variables and Parameters
        
        // Strategy Parameters
        private int lookbackPeriod = 50;
        private double riskPercentage = 1.0;
        private double atrMultiplier = 0.5;
        private int barsRequiredToTradeHigh = 2;
        private int barsRequiredToTradeLow = 2;
        private bool useFixedTickStop = true;
        private int fixedTickStopSize = 50;
        
        // Indicators
        private ATR atrIndicator;
        
        // Custom Series
        private Series<double> atrValues;
        private Series<bool> uptrend;
        private Series<bool> downtrend;
        private Series<bool> liqSweepUpDetected;
        private Series<bool> liqSweepDownDetected;
        private Series<bool> bosUp;
        private Series<bool> bosDown;
        private Series<bool> obCreated;
        private Series<double> obHigh;
        private Series<double> obLow;
        private Series<bool> obBullish;
        private Series<bool> obBearish;
        private Series<bool> fvgCreated;
        private Series<double> fvgHigh;
        private Series<double> fvgLow;
        private Series<bool> fvgBullish;
        private Series<bool> fvgBearish;
        private Series<double> eqLevel;
        
        // State tracking variables
        private bool inLong = false;
        private bool inShort = false;
        private double entryPrice = 0.0;
        private int entryBar = 0;
        private double stopLossPrice = 0.0;
        private double takeProfitPrice1 = 0.0;
        private double takeProfitPrice2 = 0.0;
        private int tradeBarsSinceEntry = 0;
        
        // Swing points storage
        private List<SwingPoint> swingHighs;
        private List<SwingPoint> swingLows;
        
        // Structure for swing points
        public class SwingPoint
        {
            public int BarIndex { get; set; }
            public double Price { get; set; }
            public bool Swept { get; set; }
            
            public SwingPoint(int barIndex, double price)
            {
                BarIndex = barIndex;
                Price = price;
                Swept = false;
            }
        }
        
        #endregion
        
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                    = @"Ingenuity Strategy based on Liquidity, BOS, OB, FVG, and EQ";
                Name                           = "IngenuityStrategy";
                Calculate                      = Calculate.OnBarClose;
                EntriesPerDirection            = 1;
                EntryHandling                  = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy   = true;
                ExitOnSessionCloseSeconds      = 30;
                IsFillLimitOnTouch             = false;
                MaximumBarsLookBack            = MaximumBarsLookBack.TwoHundredFiftySix;
                OrderFillResolution            = OrderFillResolution.Standard;
                Slippage                       = 0;
                StartBehavior                  = StartBehavior.WaitUntilFlat;
                TimeInForce                    = TimeInForce.Gtc;
                TraceOrders                    = false;
                RealtimeErrorHandling          = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling             = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade            = 20;
                
                // Add user-defined parameters here
                LookbackPeriod                 = 50;
                RiskPercentage                 = 1.0;
                ATRMultiplier                  = 0.5;
                BarsRequiredToTradeHigh        = 2;
                BarsRequiredToTradeLow         = 2;
            }
            else if (State == State.Configure)
            {
                // Initialize lists
                swingHighs = new List<SwingPoint>();
                swingLows = new List<SwingPoint>();
                
                // Add indicators
                atrIndicator = ATR(14);
                
                // Initialize series
                atrValues = new Series<double>(this);
                uptrend = new Series<bool>(this);
                downtrend = new Series<bool>(this);
                liqSweepUpDetected = new Series<bool>(this);
                liqSweepDownDetected = new Series<bool>(this);
                bosUp = new Series<bool>(this);
                bosDown = new Series<bool>(this);
                obCreated = new Series<bool>(this);
                obHigh = new Series<double>(this);
                obLow = new Series<double>(this);
                obBullish = new Series<bool>(this);
                obBearish = new Series<bool>(this);
                fvgCreated = new Series<bool>(this);
                fvgHigh = new Series<double>(this);
                fvgLow = new Series<double>(this);
                fvgBullish = new Series<bool>(this);
                fvgBearish = new Series<bool>(this);
                eqLevel = new Series<double>(this);
            }
        }

        protected override void OnBarUpdate()
        {
            // Wait for enough bars to calculate indicators
            if (CurrentBar < BarsRequiredToTrade)
                return;
            
            // Update ATR value
            atrValues[0] = atrIndicator[0];
            
            // Process core strategy components
            IdentifyTrend();
            DetectSwingPoints();
            DetectLiquiditySweeps();
            DetectBreakOfStructure();
            IdentifyOrderBlocks();
            IdentifyFairValueGaps();
            CalculateEquilibriumLevels();
            
            // Handle trading logic
            ManageExistingPositions();
            CheckForNewEntries();
            
            // Draw key levels for visualization
            DrawLevels();
        }
        
        #region Strategy Component Methods
        
        private void IdentifyTrend()
        {
            // Default trend state
            uptrend[0] = false;
            downtrend[0] = false;
            
            // Simple trend detection based on higher highs/lows or lower highs/lows
            if (CurrentBar < 4)
                return;
                
            // Higher highs and higher lows = uptrend
            if (High[0] > High[1] && Low[0] > Low[1] && High[1] > High[2] && Low[1] > Low[2])
            {
                uptrend[0] = true;
            }
            // Lower highs and lower lows = downtrend
            else if (High[0] < High[1] && Low[0] < Low[1] && High[1] < High[2] && Low[1] < Low[2])
            {
                downtrend[0] = true;
            }
        }
        
        private void DetectSwingPoints()
        {
            if (CurrentBar < 5)
                return;
                
            // Detecting swing highs (pivot highs)
            if (High[2] > High[1] && High[2] > High[3] && High[2] > High[0] && High[2] > High[4])
            {
                swingHighs.Add(new SwingPoint(CurrentBar - 2, High[2]));
                
                // For visualization
                Draw.Dot(this, "SwingHigh_" + (CurrentBar - 2).ToString(), false, 2, High[2], Brushes.DodgerBlue);
            }
            
            // Detecting swing lows (pivot lows)
            if (Low[2] < Low[1] && Low[2] < Low[3] && Low[2] < Low[0] && Low[2] < Low[4])
            {
                swingLows.Add(new SwingPoint(CurrentBar - 2, Low[2]));
                
                // For visualization
                Draw.Dot(this, "SwingLow_" + (CurrentBar - 2).ToString(), false, 2, Low[2], Brushes.Crimson);
            }
            
            // Cleanup old swing points to prevent memory issues
            if (CurrentBar % 100 == 0)
            {
                CleanupOldSwingPoints();
            }
        }
        
        private void CleanupOldSwingPoints()
        {
            int cutoffBar = Math.Max(0, CurrentBar - LookbackPeriod);
            swingHighs.RemoveAll(sp => sp.BarIndex < cutoffBar);
            swingLows.RemoveAll(sp => sp.BarIndex < cutoffBar);
        }
        
        private void DetectLiquiditySweeps()
        {
            liqSweepUpDetected[0] = false;
            liqSweepDownDetected[0] = false;
            
            if (swingHighs.Count < 2 || swingLows.Count < 2)
                return;
                
            // Get recent swing points (that aren't already swept)
            var recentHighs = swingHighs.Where(sp => !sp.Swept && sp.BarIndex < CurrentBar - 1)
                                      .OrderByDescending(sp => sp.BarIndex)
                                      .Take(3)
                                      .ToList();
                                      
            var recentLows = swingLows.Where(sp => !sp.Swept && sp.BarIndex < CurrentBar - 1)
                                     .OrderByDescending(sp => sp.BarIndex)
                                     .Take(3)
                                     .ToList();
            
            // Check for bullish liquidity sweep (price goes below recent low but closes back above)
            foreach (var swingLow in recentLows)
            {
                // Price went below swing low (sweep) but closed back above
                if (Low[0] < swingLow.Price && Close[0] > swingLow.Price)
                {
                    liqSweepDownDetected[0] = true;
                    swingLow.Swept = true;
                    
                    // Draw the liquidity sweep
                    Draw.Diamond(this, "LiqSweepDown_" + CurrentBar, false, 0, Low[0], Brushes.Green);
                    Draw.Text(this, "LiqSweepDownText_" + CurrentBar, "LIQ↑", 0, Low[0] - (5 * TickSize), Brushes.Green);
                    
                    // Break out of the loop after first sweep detection
                    break;
                }
            }
            
            // Check for bearish liquidity sweep (price goes above recent high but closes back below)
            foreach (var swingHigh in recentHighs)
            {
                // Price went above swing high (sweep) but closed back below
                if (High[0] > swingHigh.Price && Close[0] < swingHigh.Price)
                {
                    liqSweepUpDetected[0] = true;
                    swingHigh.Swept = true;
                    
                    // Draw the liquidity sweep
                    Draw.Diamond(this, "LiqSweepUp_" + CurrentBar, false, 0, High[0], Brushes.Red);
                    Draw.Text(this, "LiqSweepUpText_" + CurrentBar, "LIQ↓", 0, High[0] + (5 * TickSize), Brushes.Red);
                    
                    // Break out of the loop after first sweep detection
                    break;
                }
            }
        }
        
        private void DetectBreakOfStructure()
        {
            bosUp[0] = false;
            bosDown[0] = false;
            
            if (CurrentBar < 5)
                return;
                
            // Get most recent swing points
            var recentHighs = swingHighs.OrderByDescending(sp => sp.BarIndex).Take(2).ToList();
            var recentLows = swingLows.OrderByDescending(sp => sp.BarIndex).Take(2).ToList();
            
            // Need at least one recent swing point for both high and low
            if (recentHighs.Count == 0 || recentLows.Count == 0)
                return;
                
            // Bullish BOS - Current close breaks above the most recent lower high
            if (recentHighs.Count >= 2 && Close[0] > recentHighs[1].Price && bosUp[1] == false)
            {
                bosUp[0] = true;
                Draw.ArrowUp(this, "BosUp_" + CurrentBar, false, 0, Low[0] - (3 * atrValues[0]), Brushes.Green);
                Draw.Text(this, "BosUpText_" + CurrentBar, "BOS↑", 0, Low[0] - (4 * atrValues[0]), Brushes.Green);
            }
            
            // Bearish BOS - Current close breaks below the most recent higher low
            if (recentLows.Count >= 2 && Close[0] < recentLows[1].Price && bosDown[1] == false)
            {
                bosDown[0] = true;
                Draw.ArrowDown(this, "BosDown_" + CurrentBar, false, 0, High[0] + (3 * atrValues[0]), Brushes.Red);
                Draw.Text(this, "BosDownText_" + CurrentBar, "BOS↓", 0, High[0] + (4 * atrValues[0]), Brushes.Red);
            }
        }
        
        private void IdentifyOrderBlocks()
        {
            obCreated[0] = false;
            obBullish[0] = false;
            obBearish[0] = false;
            obHigh[0] = 0;
            obLow[0] = 0;
            
            // Check for a new bullish order block (the last down candle before a bullish BOS)
            if (bosUp[0] && bosUp[1] == false)
            {
                // Look back to find the last bearish candle before this BOS
                for (int i = 1; i < 10; i++)
                {
                    if (CurrentBar - i < 0) break;
                    
                    if (Close[i] < Open[i])  // Bearish candle
                    {
                        obCreated[0] = true;
                        obBullish[0] = true;
                        obHigh[0] = Math.Max(Open[i], Close[i]);
                        obLow[0] = Math.Min(Open[i], Close[i]);
                        
                        // Visualization of the Order Block
                        Draw.Rectangle(this, "BullishOB_" + CurrentBar, false, i, obLow[0], 0, obHigh[0], Brushes.Transparent, Brushes.Green, 50);
                        Draw.Text(this, "BullishOBText_" + CurrentBar, "Bullish OB", 0, Low[0] - (6 * atrValues[0]), Brushes.Green);
                        break;
                    }
                }
            }
            
            // Check for a new bearish order block (the last up candle before a bearish BOS)
            if (bosDown[0] && bosDown[1] == false)
            {
                // Look back to find the last bullish candle before this BOS
                for (int i = 1; i < 10; i++)
                {
                    if (CurrentBar - i < 0) break;
                    
                    if (Close[i] > Open[i])  // Bullish candle
                    {
                        obCreated[0] = true;
                        obBearish[0] = true;
                        obHigh[0] = Math.Max(Open[i], Close[i]);
                        obLow[0] = Math.Min(Open[i], Close[i]);
                        
                        // Visualization of the Order Block
                        Draw.Rectangle(this, "BearishOB_" + CurrentBar, false, i, obLow[0], 0, obHigh[0], Brushes.Transparent, Brushes.Red, 50);
                        Draw.Text(this, "BearishOBText_" + CurrentBar, "Bearish OB", 0, High[0] + (6 * atrValues[0]), Brushes.Red);
                        break;
                    }
                }
            }
        }
        
        private void IdentifyFairValueGaps()
        {
            fvgCreated[0] = false;
            fvgBullish[0] = false;
            fvgBearish[0] = false;
            fvgHigh[0] = 0;
            fvgLow[0] = 0;
            
            if (CurrentBar < 2)
                return;
                
            // Bullish FVG - Current candle's low is above the previous candle's high
            if (Low[0] > High[1])
            {
                fvgCreated[0] = true;
                fvgBullish[0] = true;
                fvgHigh[0] = Low[0];
                fvgLow[0] = High[1];
                
                // Draw the FVG
                Draw.Rectangle(this, "BullishFVG_" + CurrentBar, false, 1, fvgLow[0], 0, fvgHigh[0], Brushes.LightGreen, Brushes.Green, 30);
            }
            
            // Bearish FVG - Current candle's high is below the previous candle's low
            if (High[0] < Low[1])
            {
                fvgCreated[0] = true;
                fvgBearish[0] = true;
                fvgHigh[0] = Low[1];
                fvgLow[0] = High[0];
                
                // Draw the FVG
                Draw.Rectangle(this, "BearishFVG_" + CurrentBar, false, 1, fvgLow[0], 0, fvgHigh[0], Brushes.Pink, Brushes.Red, 30);
            }
        }
        
        private void CalculateEquilibriumLevels()
        {
            eqLevel[0] = 0;
            
            // Find the highest high and lowest low in the recent lookback period
            double highestHigh = double.MinValue;
            double lowestLow = double.MaxValue;
            
            for (int i = 1; i <= 10; i++)
            {
                if (CurrentBar < i) continue;
                
                highestHigh = Math.Max(highestHigh, High[i]);
                lowestLow = Math.Min(lowestLow, Low[i]);
            }
            
            // Calculate the 50% equilibrium level
            eqLevel[0] = lowestLow + ((highestHigh - lowestLow) * 0.5);
            
            // Draw the EQ level
            if (CurrentBar % 5 == 0)  // Only redraw occasionally to reduce visual clutter
            {
                Draw.Line(this, "EQ_" + CurrentBar, false, 10, eqLevel[0], 0, eqLevel[0], Brushes.Purple, DashStyleHelper.Dash, 1);
                Draw.Text(this, "EQText_" + CurrentBar, "EQ", 0, eqLevel[0] + (2 * TickSize), Brushes.Purple);
            }
        }
        
        #endregion
        
        #region Trading Logic
        
        private void CheckForNewEntries()
        {
            // Skip if already in a position
            if (inLong || inShort)
                return;
                
            // Entry Strategy A: LIQ Sweep + BOS
            CheckLiqSweepBosEntry();
            
            // Entry Strategy B: LIQ Sweep + BOS + Retrace into OB
            CheckLiqSweepBosObEntry();
            
            // Entry Strategy C: LIQ Sweep + BOS + FVG Filled
            CheckLiqSweepBosFvgEntry();
            
            // Entry Strategy D: LIQ Sweep + BOS + FVG/OB + EQ (Ultimate Confluence)
            CheckUltimateConfluenceEntry();
        }
        
        private void CheckLiqSweepBosEntry()
        {
            // Bullish entry: Liquidity sweep down followed by break of structure up
            if (liqSweepDownDetected[0] && bosUp[0])
            {
                ExecuteLongEntry("LiqSweepBos");
            }
            
            // Bearish entry: Liquidity sweep up followed by break of structure down
            if (liqSweepUpDetected[0] && bosDown[0])
            {
                ExecuteShortEntry("LiqSweepBos");
            }
        }
        
        private void CheckLiqSweepBosObEntry()
        {
            // Need a previous bullish order block for a long trade
            if (HasBullishOrderBlock() && HasLiquiditySweepDown() && HasBreakOfStructureUp())
            {
                // Find the most recent bullish OB
                double obHighLevel = 0;
                double obLowLevel = 0;
                
                for (int i = 0; i < 20; i++)
                {
                    if (CurrentBar - i < 0) break;
                    
                    if (obBullish[i])
                    {
                        obHighLevel = obHigh[i];
                        obLowLevel = obLow[i];
                        break;
                    }
                }
                
                // Price is retracing into the order block
                if (obHighLevel > 0 && obLowLevel > 0 && 
                    Low[0] <= obHighLevel && Close[0] >= obLowLevel)
                {
                    ExecuteLongEntry("LiqSweepBosOb");
                }
            }
            
            // Need a previous bearish order block for a short trade
            if (HasBearishOrderBlock() && HasLiquiditySweepUp() && HasBreakOfStructureDown())
            {
                // Find the most recent bearish OB
                double obHighLevel = 0;
                double obLowLevel = 0;
                
                for (int i = 0; i < 20; i++)
                {
                    if (CurrentBar - i < 0) break;
                    
                    if (obBearish[i])
                    {
                        obHighLevel = obHigh[i];
                        obLowLevel = obLow[i];
                        break;
                    }
                }
                
                // Price is retracing into the order block
                if (obHighLevel > 0 && obLowLevel > 0 && 
                    High[0] >= obLowLevel && Close[0] <= obHighLevel)
                {
                    ExecuteShortEntry("LiqSweepBosOb");
                }
            }
        }
        
        private void CheckLiqSweepBosFvgEntry()
        {
            // Need a previous bullish FVG for a long trade
            if (HasBullishFVG() && HasLiquiditySweepDown() && HasBreakOfStructureUp())
            {
                // Find the most recent bullish FVG
                double fvgHighLevel = 0;
                double fvgLowLevel = 0;
                
                for (int i = 0; i < 20; i++)
                {
                    if (CurrentBar - i < 0) break;
                    
                    if (fvgBullish[i])
                    {
                        fvgHighLevel = fvgHigh[i];
                        fvgLowLevel = fvgLow[i];
                        break;
                    }
                }
                
                // Price is filling the FVG
                if (fvgHighLevel > 0 && fvgLowLevel > 0 && 
                    Low[0] <= fvgHighLevel && Low[0] >= fvgLowLevel)
                {
                    ExecuteLongEntry("LiqSweepBosFvg");
                }
            }
            
            // Need a previous bearish FVG for a short trade
            if (HasBearishFVG() && HasLiquiditySweepUp() && HasBreakOfStructureDown())
            {
                // Find the most recent bearish FVG
                double fvgHighLevel = 0;
                double fvgLowLevel = 0;
                
                for (int i = 0; i < 20; i++)
                {
                    if (CurrentBar - i < 0) break;
                    
                    if (fvgBearish[i])
                    {
                        fvgHighLevel = fvgHigh[i];
                        fvgLowLevel = fvgLow[i];
                        break;
                    }
                }
                
                // Price is filling the FVG
                if (fvgHighLevel > 0 && fvgLowLevel > 0 && 
                    High[0] >= fvgLowLevel && High[0] <= fvgHighLevel)
                {
                    ExecuteShortEntry("LiqSweepBosFvg");
                }
            }
        }
        
        private void CheckUltimateConfluenceEntry()
        {
            // Ultimate confluence: LIQ + BOS + OB/FVG + EQ
            // For long entries
            if (HasLiquiditySweepDown() && HasBreakOfStructureUp() && eqLevel[0] > 0)
            {
                // Price is near or below EQ (discount)
                if (Close[0] <= eqLevel[0])
                {
                    // Check for OB or FVG confluence
                    bool hasObConfluence = false;
                    bool hasFvgConfluence = false;
                    
                    // Check for OB confluence
                    if (HasBullishOrderBlock())
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            if (CurrentBar - i < 0) break;
                            
                            if (obBullish[i] && Low[0] <= obHigh[i] && Close[0] >= obLow[i])
                            {
                                hasObConfluence = true;
                                break;
                            }
                        }
                    }
                    
                    // Check for FVG confluence
                    if (HasBullishFVG())
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            if (CurrentBar - i < 0) break;
                            
                            if (fvgBullish[i] && Low[0] <= fvgHigh[i] && Low[0] >= fvgLow[i])
                            {
                                hasFvgConfluence = true;
                                break;
                            }
                        }
                    }
                    
                    if (hasObConfluence || hasFvgConfluence)
                    {
                        ExecuteLongEntry("UltimateConfluence");
                    }
                }
            }
            
            // For short entries
            if (HasLiquiditySweepUp() && HasBreakOfStructureDown() && eqLevel[0] > 0)
            {
                // Price is near or above EQ (premium)
                if (Close[0] >= eqLevel[0])
                {
                    // Check for OB or FVG confluence
                    bool hasObConfluence = false;
                    bool hasFvgConfluence = false;
                    
                    // Check for OB confluence
                    if (HasBearishOrderBlock())
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            if (CurrentBar - i < 0) break;
                            
                            if (obBearish[i] && High[0] >= obLow[i] && Close[0] <= obHigh[i])
                            {
                                hasObConfluence = true;
                                break;
                            }
                        }
                    }
                    
                    // Check for FVG confluence
                    if (HasBearishFVG())
                    {
                        for (int i = 0; i < 20; i++)
                        {
                            if (CurrentBar - i < 0) break;
                            
                            if (fvgBearish[i] && High[0] >= fvgLow[i] && High[0] <= fvgHigh[i])
                            {
                                hasFvgConfluence = true;
                                break;
                            }
                        }
                    }
                    
                    if (hasObConfluence || hasFvgConfluence)
                    {
                        ExecuteShortEntry("UltimateConfluence");
                    }
                }
            }
        }
        
        private void ExecuteLongEntry(string reason)
        {
            // Find the most recent liquidity sweep for stop placement
            double sweepLow = 0;
            
            // Get the most recent swept low
            foreach (var swingLow in swingLows.Where(sp => sp.Swept).OrderByDescending(sp => sp.BarIndex))
            {
                sweepLow = swingLow.Price;
                break;
            }
            
            // Fallback if no swept low was found
            if (sweepLow == 0)
            {
                sweepLow = Low[0];
            }
            
            // Define stop loss level - below the recent sweep
            double initialStop = sweepLow - (ATRMultiplier * atrValues[0]);
            
            // Add fixed 50 tick stop loss protection
            double fixedStopDistance = 50 * TickSize;
            double fixedStopPrice = Close[0] - fixedStopDistance;
            
            // Use the tighter of the two stops
            stopLossPrice = Math.Max(initialStop, fixedStopPrice);
            
            Print("Long Entry at " + Close[0] + " - Initial Stop: " + initialStop + " - Fixed Stop: " + fixedStopPrice + " - Final Stop: " + stopLossPrice);
            
            // Calculate take profit levels based on surrounding liquidity levels
            double tp1Distance = (Close[0] - stopLossPrice) * 1.5; // 1.5:1 RR for first TP
            double tp2Distance = (Close[0] - stopLossPrice) * 2.5; // 2.5:1 RR for second TP
            
            takeProfitPrice1 = Close[0] + tp1Distance;
            takeProfitPrice2 = Close[0] + tp2Distance;
            
            // Calculate position size based on risk
            double riskAmount = Account.Get(AccountItem.CashValue, Instrument.MasterInstrument.Currency) * (RiskPercentage / 100.0);
            double riskPips = Math.Abs(Close[0] - stopLossPrice);
            double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
            int quantity = (int)Math.Floor(riskAmount / (riskPips * tickValue));
            
            // Ensure minimum quantity
            quantity = Math.Max(1, quantity);
            
            // Enter the position
            EnterLong(quantity, reason);
            
            // Update state variables
            inLong = true;
            entryPrice = Close[0];
            entryBar = CurrentBar;
            tradeBarsSinceEntry = 0;
            
            // Draw entry indicators
            Draw.TriangleUp(this, "LongEntry_" + CurrentBar, false, 0, Low[0] - (2 * atrValues[0]), Brushes.LimeGreen);
            Draw.Text(this, "LongEntryText_" + CurrentBar, reason, 0, Low[0] - (3 * atrValues[0]), Brushes.White);
            
            // Draw stop loss
            Draw.Line(this, "StopLoss_" + CurrentBar, false, 0, stopLossPrice, 10, stopLossPrice, Brushes.Red, DashStyleHelper.Solid, 2);
            Draw.Text(this, "StopLossText_" + CurrentBar, "SL", 0, stopLossPrice, Brushes.Red);
            
            // Draw take profits
            Draw.Line(this, "TP1_" + CurrentBar, false, 0, takeProfitPrice1, 10, takeProfitPrice1, Brushes.Green, DashStyleHelper.Dash, 1);
            Draw.Text(this, "TP1Text_" + CurrentBar, "TP1", 0, takeProfitPrice1, Brushes.Green);
            
            Draw.Line(this, "TP2_" + CurrentBar, false, 0, takeProfitPrice2, 10, takeProfitPrice2, Brushes.Green, DashStyleHelper.Dash, 1);
            Draw.Text(this, "TP2Text_" + CurrentBar, "TP2", 0, takeProfitPrice2, Brushes.Green);
        }
        
        private void ExecuteShortEntry(string reason)
        {
            // Find the most recent liquidity sweep for stop placement
            double sweepHigh = 0;
            
            // Get the most recent swept high
            foreach (var swingHigh in swingHighs.Where(sp => sp.Swept).OrderByDescending(sp => sp.BarIndex))
            {
                sweepHigh = swingHigh.Price;
                break;
            }
            
            // Fallback if no swept high was found
            if (sweepHigh == 0)
            {
                sweepHigh = High[0];
            }
            
            // Define stop loss level - above the recent sweep
            double initialStop = sweepHigh + (ATRMultiplier * atrValues[0]);
            
            // Add fixed 50 tick stop loss protection
            double fixedStopDistance = 50 * TickSize;
            double fixedStopPrice = Close[0] + fixedStopDistance;
            
            // Use the tighter of the two stops
            stopLossPrice = Math.Min(initialStop, fixedStopPrice);
            
            Print("Short Entry at " + Close[0] + " - Initial Stop: " + initialStop + " - Fixed Stop: " + fixedStopPrice + " - Final Stop: " + stopLossPrice);
            
            // Calculate take profit levels based on surrounding liquidity levels
            double tp1Distance = (stopLossPrice - Close[0]) * 1.5; // 1.5:1 RR for first TP
            double tp2Distance = (stopLossPrice - Close[0]) * 2.5; // 2.5:1 RR for second TP
            
            takeProfitPrice1 = Close[0] - tp1Distance;
            takeProfitPrice2 = Close[0] - tp2Distance;
            
            // Calculate position size based on risk
            double riskAmount = Account.Get(AccountItem.CashValue, Instrument.MasterInstrument.Currency) * (RiskPercentage / 100.0);
            double riskPips = Math.Abs(stopLossPrice - Close[0]);
            double tickValue = Instrument.MasterInstrument.PointValue * TickSize;
            int quantity = (int)Math.Floor(riskAmount / (riskPips * tickValue));
            
            // Ensure minimum quantity
            quantity = Math.Max(1, quantity);
            
            // Enter the position
            EnterShort(quantity, reason);
            
            // Update state variables
            inShort = true;
            entryPrice = Close[0];
            entryBar = CurrentBar;
            tradeBarsSinceEntry = 0;
            
            // Draw entry indicators
            Draw.TriangleDown(this, "ShortEntry_" + CurrentBar, false, 0, High[0] + (2 * atrValues[0]), Brushes.Crimson);
            Draw.Text(this, "ShortEntryText_" + CurrentBar, reason, 0, High[0] + (3 * atrValues[0]), Brushes.White);
            
            // Draw stop loss
            Draw.Line(this, "StopLoss_" + CurrentBar, false, 0, stopLossPrice, 10, stopLossPrice, Brushes.Red, DashStyleHelper.Solid, 2);
            Draw.Text(this, "StopLossText_" + CurrentBar, "SL", 0, stopLossPrice, Brushes.Red);
            
            // Draw take profits
            Draw.Line(this, "TP1_" + CurrentBar, false, 0, takeProfitPrice1, 10, takeProfitPrice1, Brushes.Green, DashStyleHelper.Dash, 1);
            Draw.Text(this, "TP1Text_" + CurrentBar, "TP1", 0, takeProfitPrice1, Brushes.Green);
            
            Draw.Line(this, "TP2_" + CurrentBar, false, 0, takeProfitPrice2, 10, takeProfitPrice2, Brushes.Green, DashStyleHelper.Dash, 1);
            Draw.Text(this, "TP2Text_" + CurrentBar, "TP2", 0, takeProfitPrice2, Brushes.Green);
        }
        
        private void ManageExistingPositions()
        {
            if (!inLong && !inShort)
                return;
                
            if (inLong)
            {
                tradeBarsSinceEntry++;
                
                // Exit rules for long positions
                
                // 1. Stop loss hit
                if (Low[0] <= stopLossPrice)
                {
                    ExitLong("StopLoss");
                    ResetPositionState();
                    Draw.Text(this, "ExitText_" + CurrentBar, "SL Exit", 0, Low[0], Brushes.Red);
                    return;
                }
                
                // 2. Take profit 1 hit (partial exit)
                if (High[0] >= takeProfitPrice1 && tradeBarsSinceEntry > 1)
                {
                    // Exit half the position at TP1
                    ExitLong(Position.Quantity / 2);
                    Draw.Text(this, "TP1Hit_" + CurrentBar, "TP1 Hit", 0, takeProfitPrice1, Brushes.Green);
                    
                    // Move stop loss to break even after TP1 is hit
                    stopLossPrice = entryPrice;
                    Draw.Line(this, "NewStopLoss_" + CurrentBar, false, 0, stopLossPrice, 10, stopLossPrice, Brushes.Orange, DashStyleHelper.Solid, 2);
                }
                
                // 3. Take profit 2 hit (exit remaining position)
                if (High[0] >= takeProfitPrice2 && tradeBarsSinceEntry > 1)
                {
                    ExitLong("TP2");
                    ResetPositionState();
                    Draw.Text(this, "TP2Hit_" + CurrentBar, "TP2 Hit", 0, takeProfitPrice2, Brushes.Green);
                    return;
                }
                
                // 4. Trailing stop based on recent structure
                if (tradeBarsSinceEntry > 5)
                {
                    // Find the lowest low of the last 3 bars (simple trailing stop logic)
                    double trailingStop = double.MaxValue;
                    for (int i = 1; i <= 3; i++)
                    {
                        if (CurrentBar >= i)
                            trailingStop = Math.Min(trailingStop, Low[i]);
                    }
                    
                    // Make sure trailing stop isn't too far from current price
                    double maxTrailDistance = Math.Min(Close[0] - entryPrice, 50 * TickSize);
                    if (Close[0] - trailingStop > maxTrailDistance)
                    {
                        trailingStop = Close[0] - maxTrailDistance;
                    }
                    
                    // Only move stop loss upward, never downward
                    if (trailingStop > stopLossPrice && trailingStop < Close[0])
                    {
                        stopLossPrice = trailingStop;
                        Draw.Line(this, "TrailStop_" + CurrentBar, false, 0, stopLossPrice, 10, stopLossPrice, Brushes.Orange, DashStyleHelper.Dash, 2);
                    }
                }
                
                // Emergency stop loss - ensure stop is never more than 50 ticks away
                double emergencyStop = entryPrice - (fixedTickStopSize * TickSize);
                if (stopLossPrice < emergencyStop)
                {
                    stopLossPrice = emergencyStop;
                    Draw.Line(this, "EmergencyStop_" + CurrentBar, false, 0, stopLossPrice, 10, stopLossPrice, Brushes.Red, DashStyleHelper.Solid, 2);
                }
            }
            
            if (inShort)
            {
                tradeBarsSinceEntry++;
                
                // Exit rules for short positions
                
                // 1. Stop loss hit
                if (High[0] >= stopLossPrice)
                {
                    ExitShort("StopLoss");
                    ResetPositionState();
                    Draw.Text(this, "ExitText_" + CurrentBar, "SL Exit", 0, High[0], Brushes.Red);
                    return;
                }
                
                // 2. Take profit 1 hit (partial exit)
                if (Low[0] <= takeProfitPrice1 && tradeBarsSinceEntry > 1)
                {
                    // Exit half the position at TP1
                    ExitShort(Position.Quantity / 2);
                    Draw.Text(this, "TP1Hit_" + CurrentBar, "TP1 Hit", 0, takeProfitPrice1, Brushes.Green);
                    
                    // Move stop loss to break even after TP1 is hit
                    stopLossPrice = entryPrice;
                    Draw.Line(this, "NewStopLoss_" + CurrentBar, false, 0, stopLossPrice, 10, stopLossPrice, Brushes.Orange, DashStyleHelper.Solid, 2);
                }
                
                // 3. Take profit 2 hit (exit remaining position)
                if (Low[0] <= takeProfitPrice2 && tradeBarsSinceEntry > 1)
                {
                    ExitShort("TP2");
                    ResetPositionState();
                    Draw.Text(this, "TP2Hit_" + CurrentBar, "TP2 Hit", 0, takeProfitPrice2, Brushes.Green);
                    return;
                }
                
                // 4. Trailing stop based on recent structure
                if (tradeBarsSinceEntry > 5)
                {
                    // Find the highest high of the last 3 bars (simple trailing stop logic)
                    double trailingStop = double.MinValue;
                    for (int i = 1; i <= 3; i++)
                    {
                        if (CurrentBar >= i)
                            trailingStop = Math.Max(trailingStop, High[i]);
                    }
                    
                    // Make sure trailing stop isn't too far from current price
                    double maxTrailDistance = Math.Min(entryPrice - Close[0], 50 * TickSize);
                    if (trailingStop - Close[0] > maxTrailDistance)
                    {
                        trailingStop = Close[0] + maxTrailDistance;
                    }
                    
                    // Only move stop loss downward, never upward
                    if (trailingStop < stopLossPrice && trailingStop > Close[0])
                    {
                        stopLossPrice = trailingStop;
                        Draw.Line(this, "TrailStop_" + CurrentBar, false, 0, stopLossPrice, 10, stopLossPrice, Brushes.Orange, DashStyleHelper.Dash, 2);
                    }
                }
                
                // Emergency stop loss - ensure stop is never more than 50 ticks away
                double emergencyStop = entryPrice + (fixedTickStopSize * TickSize);
                if (stopLossPrice > emergencyStop)
                {
                    stopLossPrice = emergencyStop;
                    Draw.Line(this, "EmergencyStop_" + CurrentBar, false, 0, stopLossPrice, 10, stopLossPrice, Brushes.Red, DashStyleHelper.Solid, 2);
                }
            }
        }
        
        private void ResetPositionState()
        {
            inLong = false;
            inShort = false;
            entryPrice = 0.0;
            stopLossPrice = 0.0;
            takeProfitPrice1 = 0.0;
            takeProfitPrice2 = 0.0;
            tradeBarsSinceEntry = 0;
        }
        
        #endregion
        
        #region Helper Methods
        
        private bool HasLiquiditySweepDown()
        {
            for (int i = 0; i < 20; i++)
            {
                if (CurrentBar < i) return false;
                if (liqSweepDownDetected[i]) return true;
            }
            return false;
        }
        
        private bool HasLiquiditySweepUp()
        {
            for (int i = 0; i < 20; i++)
            {
                if (CurrentBar < i) return false;
                if (liqSweepUpDetected[i]) return true;
            }
            return false;
        }
        
        private bool HasBreakOfStructureUp()
        {
            for (int i = 0; i < 20; i++)
            {
                if (CurrentBar < i) return false;
                if (bosUp[i]) return true;
            }
            return false;
        }
        
        private bool HasBreakOfStructureDown()
        {
            for (int i = 0; i < 20; i++)
            {
                if (CurrentBar < i) return false;
                if (bosDown[i]) return true;
            }
            return false;
        }
        
        private bool HasBullishOrderBlock()
        {
            for (int i = 0; i < 20; i++)
            {
                if (CurrentBar < i) return false;
                if (obBullish[i]) return true;
            }
            return false;
        }
        
        private bool HasBearishOrderBlock()
        {
            for (int i = 0; i < 20; i++)
            {
                if (CurrentBar < i) return false;
                if (obBearish[i]) return true;
            }
            return false;
        }
        
        private bool HasBullishFVG()
        {
            for (int i = 0; i < 20; i++)
            {
                if (CurrentBar < i) return false;
                if (fvgBullish[i]) return true;
            }
            return false;
        }
        
        private bool HasBearishFVG()
        {
            for (int i = 0; i < 20; i++)
            {
                if (CurrentBar < i) return false;
                if (fvgBearish[i]) return true;
            }
            return false;
        }
        
        #endregion
        
        #region Drawing and Visualization
        
        private void DrawLevels()
        {
            // This method can be expanded to draw additional levels or indicators
            // for visualization purposes. Most drawing is already done in the
            // respective component methods.
        }
        
        #endregion
        
        #region Properties
        
        [Display(Name = "Lookback Period", Description = "Number of bars to look back for swing points", Order = 1, GroupName = "Strategy Parameters")]
        [Range(10, 200)]
        public int LookbackPeriod
        {
            get { return lookbackPeriod; }
            set { lookbackPeriod = value; }
        }
        
        [Display(Name = "Risk Percentage", Description = "Percentage of account to risk per trade", Order = 2, GroupName = "Strategy Parameters")]
        [Range(0.1, 5.0)]
        public double RiskPercentage
        {
            get { return riskPercentage; }
            set { riskPercentage = value; }
        }
        
        [Display(Name = "ATR Multiplier", Description = "Multiplier for ATR to set stop buffer", Order = 3, GroupName = "Strategy Parameters")]
        [Range(0.1, 3.0)]
        public double ATRMultiplier
        {
            get { return atrMultiplier; }
            set { atrMultiplier = value; }
        }
        
        [Display(Name = "Bars Required for High", Description = "Number of bars to confirm a swing high", Order = 4, GroupName = "Strategy Parameters")]
        [Range(1, 10)]
        public int BarsRequiredToTradeHigh
        {
            get { return barsRequiredToTradeHigh; }
            set { barsRequiredToTradeHigh = value; }
        }
        
        [Display(Name = "Bars Required for Low", Description = "Number of bars to confirm a swing low", Order = 5, GroupName = "Strategy Parameters")]
        [Range(1, 10)]
        public int BarsRequiredToTradeLow
        {
            get { return barsRequiredToTradeLow; }
            set { barsRequiredToTradeLow = value; }
        }
        
        [Display(Name = "Use Fixed Tick Stop", Description = "Enable fixed tick stop loss", Order = 6, GroupName = "Risk Management")]
        public bool UseFixedTickStop
        {
            get { return useFixedTickStop; }
            set { useFixedTickStop = value; }
        }
        
        [Display(Name = "Fixed Tick Stop Size", Description = "Maximum stop loss distance in ticks", Order = 7, GroupName = "Risk Management")]
        [Range(5, 200)]
        public int FixedTickStopSize
        {
            get { return fixedTickStopSize; }
            set { fixedTickStopSize = value; }
        }
        
        #endregion
    }
}