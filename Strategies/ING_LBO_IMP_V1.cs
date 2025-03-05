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
    public class Ingenuity_LiqBosOb_Optimized : Strategy
    {
        #region Variables and Parameters
        
        // Strategy Parameters
        private int lookbackPeriod = 50;
        private double atrMultiplier = 0.5;
        private int barsRequiredToTradeHigh = 2;
        private int barsRequiredToTradeLow = 2;
        private bool useFixedTickStop = true;
        private int fixedTickStopSize = 50;
        private bool useLiqSweepBosObEntry = true;
        private int fixedQuantity = 1; // Fixed quantity parameter
        private bool enableTimeFilter = true;
        private bool enableVolumeFilter = true;
        private bool enableTrendFilter = true;
        private bool enableQualityFilter = true;
        private int maxDailyTrades = 3;
        private int qualityScoreThreshold = 70;
        private bool useImprovedTrailingStop = true;
        private int consecutiveLossesAllowed = 2;
        
        // Indicators
        private ATR atrIndicator;
        private EMA volumeAverage;
        private EMA ema50;
        private EMA ema200;
        private ADX adxIndicator;
        
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
        
        // State tracking variables
        private bool inLong = false;
        private bool inShort = false;
        private double entryPrice = 0.0;
        private int entryBar = 0;
        private double stopLossPrice = 0.0;
        private double takeProfitPrice1 = 0.0;
        private double takeProfitPrice2 = 0.0;
        private int tradeBarsSinceEntry = 0;
        
        // Risk management variables
        private int consecutiveLosses = 0;
        private int dailyTrades = 0;
        private DateTime currentTradeDay = DateTime.MinValue;
        private DateTime tradeBreakEndTime = DateTime.MinValue;
        private bool lastTradeProfitable = false;
        
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
                Description                    = @"Ingenuity Strategy optimized for 5-minute charts with enhanced risk management";
                Name                           = "LBO OPT";
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
                BarsRequiredToTrade            = 200; // Increased to allow for indicators
                
                // User-defined parameters
                LookbackPeriod                 = 50;
                ATRMultiplier                  = 0.5;
                BarsRequiredToTradeHigh        = 2;
                BarsRequiredToTradeLow         = 2;
                UseFixedTickStop               = true;
                FixedTickStopSize              = 50;
                UseLiqSweepBosObEntry          = true;
                FixedQuantity                  = 1;
                
                // New optimization parameters
                EnableTimeFilter               = true;
                EnableVolumeFilter             = true;
                EnableTrendFilter              = true;
                EnableQualityFilter            = true;
                MaxDailyTrades                 = 3;
                QualityScoreThreshold          = 70;
                UseImprovedTrailingStop        = true;
                ConsecutiveLossesAllowed       = 2;
            }
            else if (State == State.Configure)
            {
                // Initialize lists
                swingHighs = new List<SwingPoint>();
                swingLows = new List<SwingPoint>();
                
                // Add indicators
                atrIndicator = ATR(14);
                volumeAverage = EMA(Volume, 20);
                ema50 = EMA(50);
                ema200 = EMA(200);
                adxIndicator = ADX(14);
                
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
            }
        }

        protected override void OnBarUpdate()
        {
            // Wait for enough bars to calculate indicators
            if (CurrentBar < BarsRequiredToTrade)
                return;
            
            // Update ATR and other values
            atrValues[0] = atrIndicator[0];
            
            // Process core strategy components
            IdentifyTrend();
            DetectSwingPoints();
            DetectLiquiditySweeps();
            DetectBreakOfStructure();
            IdentifyOrderBlocks();
            
            // Handle trading logic
            ManageExistingPositions();
            
            // Apply pre-trade filters before checking for new entries
            if (IsTradeAllowed())
            {
                CheckForNewEntries();
            }
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
            }
            
            // Detecting swing lows (pivot lows)
            if (Low[2] < Low[1] && Low[2] < Low[3] && Low[2] < Low[0] && Low[2] < Low[4])
            {
                swingLows.Add(new SwingPoint(CurrentBar - 2, Low[2]));
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
            }
            
            // Bearish BOS - Current close breaks below the most recent higher low
            if (recentLows.Count >= 2 && Close[0] < recentLows[1].Price && bosDown[1] == false)
            {
                bosDown[0] = true;
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
                        break;
                    }
                }
            }
        }
        
        #endregion
        
        #region Trading Logic and Filters
        
        private bool IsTradeAllowed()
        {
            // Add debug output to see which filters are rejecting trades
            Print("Checking trade filters: Time=" + Time[0].ToString() + ", Position=" + Position.MarketPosition);
            
            // Check for time filter if enabled
            if (EnableTimeFilter && !IsWithinTradingHours())
            {
                Print("Trade rejected by time filter");
                return false;
            }
                
            // Check if we're in a trade break after consecutive losses
            if (DateTime.Now < tradeBreakEndTime)
            {
                Print("In trade timeout until: " + tradeBreakEndTime.ToString("HH:mm:ss"));
                return false;
            }
                
            // Check daily trade limit
            if (Time[0].Date != currentTradeDay.Date)
            {
                currentTradeDay = Time[0].Date;
                dailyTrades = 0;
            }
            
            if (dailyTrades >= MaxDailyTrades)
            {
                Print("Max daily trades (" + MaxDailyTrades + ") reached for " + currentTradeDay.ToShortDateString());
                return false;
            }
            
            // Check volume filter if enabled
            if (EnableVolumeFilter && !HasSufficientVolume())
            {
                Print("Trade rejected by volume filter");
                return false;
            }
                
            // Check trend filter if enabled
            if (EnableTrendFilter && !IsTrendAligned())
            {
                Print("Trade rejected by trend filter");
                return false;
            }
            
            Print("All filters passed - trade allowed");
            return true;
        }
        
        private bool IsWithinTradingHours()
        {
            // Only trade during the most liquid market hours 
            // 9:30 AM - 11:30 AM and 1:30 PM - 3:30 PM ET
            TimeSpan barTime = Time[0].TimeOfDay;
            
            // Morning session: 9:30 AM - 11:30 AM
            TimeSpan morningStart = new TimeSpan(9, 30, 0);
            TimeSpan morningEnd = new TimeSpan(11, 30, 0);
            
            // Afternoon session: 1:30 PM - 3:30 PM
            TimeSpan afterStart = new TimeSpan(13, 30, 0);
            TimeSpan afterEnd = new TimeSpan(15, 30, 0);
            
            // Check if current time is within either session
            if ((barTime >= morningStart && barTime <= morningEnd) || 
                (barTime >= afterStart && barTime <= afterEnd))
                return true;
                
            return false;
        }
        
        private bool HasSufficientVolume()
        {
            // Check if current volume is above average
            return Volume[0] > volumeAverage[0] * 1.1;
        }
        
        private bool IsTrendAligned()
        {
            // Is price in the right position relative to EMAs
            bool priceTrendAligned = false;
            
            // For long trades: price above 50 EMA
            if ((uptrend[0] || HasBreakOfStructureUp()) && Close[0] > ema50[0])
                priceTrendAligned = true;
                
            // For short trades: price below 50 EMA
            if ((downtrend[0] || HasBreakOfStructureDown()) && Close[0] < ema50[0])
                priceTrendAligned = true;
                
            // Check ADX for trend strength
            bool strongTrend = adxIndicator[0] > 20;
            
            return priceTrendAligned && strongTrend;
        }
        
        private int CalculateEntryQualityScore()
        {
            int score = 0;
            
            // Trend alignment score (0-20)
            if ((Close[0] > ema50[0] && HasBreakOfStructureUp()) || 
                (Close[0] < ema50[0] && HasBreakOfStructureDown()))
                score += 20;
            
            // ADX trend strength score (0-20)
            if (adxIndicator[0] > 30) score += 20;
            else if (adxIndicator[0] > 25) score += 15;
            else if (adxIndicator[0] > 20) score += 10;
            
            // Volume score (0-20)
            if (Volume[0] > volumeAverage[0] * 1.5) score += 20;
            else if (Volume[0] > volumeAverage[0] * 1.2) score += 15;
            else if (Volume[0] > volumeAverage[0]) score += 10;
            
            // Market structure score (0-20)
            if (HasLiquiditySweepDown() && HasBreakOfStructureUp() && HasBullishOrderBlock()) score += 20;
            else if (HasLiquiditySweepUp() && HasBreakOfStructureDown() && HasBearishOrderBlock()) score += 20;
            
            // Time of day score (0-20)
            TimeSpan localTime = Time[0].TimeOfDay;
            TimeSpan morningSession = new TimeSpan(9, 30, 0); 
            TimeSpan midMorning = new TimeSpan(10, 30, 0);
            TimeSpan lateDay = new TimeSpan(15, 0, 0);
            
            if (localTime >= morningSession && localTime <= midMorning)
                score += 20;
            else if (localTime >= lateDay && localTime <= new TimeSpan(15, 30, 0))
                score += 15;
            
            return score;
        }
        
        private void CheckForNewEntries()
        {
            // Skip if already in a position
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                Print("Already in position - no new entries");
                return;
            }
            
            // Only use LiqSweepBosOb entry condition
            bool canEnterLiqSweepBosOb = false;
            
            // Check LiqSweepBosOb condition
            if (UseLiqSweepBosObEntry)
            {
                CheckLiqSweepBosObCondition(ref canEnterLiqSweepBosOb);
            }
            
            // Check entry quality score if filter is enabled
            if (canEnterLiqSweepBosOb && EnableQualityFilter)
            {
                int qualityScore = CalculateEntryQualityScore();
                if (qualityScore < QualityScoreThreshold)
                {
                    Print("Entry signal rejected - Quality score " + qualityScore + " below threshold " + QualityScoreThreshold);
                    return;
                }
                else
                {
                    Print("Entry signal accepted - Quality score: " + qualityScore);
                }
            }
            
            // Execute entry if condition is met
            if (canEnterLiqSweepBosOb)
            {
                Print("EXECUTING TRADE: New entry signal detected");
                ExecuteEntry("LiqSweepBosOb");
                dailyTrades++;
            }
            else
            {
                Print("No valid entry conditions met");
            }
        }
        
        // Check LiqSweepBosOb condition without executing entry
        private void CheckLiqSweepBosObCondition(ref bool canEnter)
        {
            canEnter = false;
            
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
                    canEnter = true;
                }
            }
            
            // Need a previous bearish order block for a short trade
            if (!canEnter && HasBearishOrderBlock() && HasLiquiditySweepUp() && HasBreakOfStructureDown())
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
                    canEnter = true;
                }
            }
        }
        
        private void ExecuteEntry(string reason)
        {
            if (uptrend[0] || HasBreakOfStructureUp())
            {
                ExecuteLongEntry(reason);
            }
            else if (downtrend[0] || HasBreakOfStructureDown())
            {
                ExecuteShortEntry(reason);
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
            
            // Add fixed tick stop loss protection
            double fixedStopDistance = FixedTickStopSize * TickSize;
            double fixedStopPrice = Close[0] - fixedStopDistance;
            
            // Use the tighter of the two stops
            stopLossPrice = Math.Max(initialStop, fixedStopPrice);
            
            // Check if the stop is too far - adjust risk by reducing position size if needed
            double riskPips = Math.Abs(Close[0] - stopLossPrice);
            int adjustedQuantity = FixedQuantity;
            
            // For higher volatility, consider reducing position size
            double avgAtrValue = SMA(atrValues, 20)[0];
            bool isHighVolatility = atrValues[0] > avgAtrValue * 1.5;
            if (isHighVolatility)
            {
                adjustedQuantity = Math.Max(1, FixedQuantity / 2);
                Print("Reducing position size due to high volatility");
            }
            
            Print("Long Entry at " + Close[0] + " - Stop: " + stopLossPrice +
                  " - Quantity: " + adjustedQuantity + " contracts");
            
            // Calculate adaptive take profit levels
            bool isVolatile = atrValues[0] > avgAtrValue * 1.2;
            
            // In higher volatility, use wider targets
            double tp1RR = isVolatile ? 1.5 : 1.0;
            double tp2RR = isVolatile ? 2.5 : 1.5;
            
            double tp1Distance = (Close[0] - stopLossPrice) * tp1RR;
            double tp2Distance = (Close[0] - stopLossPrice) * tp2RR;
            
            takeProfitPrice1 = Close[0] + tp1Distance;
            takeProfitPrice2 = Close[0] + tp2Distance;
            
            // Enter the position
            EnterLong(adjustedQuantity, reason);
            
            // Update state variables
            inLong = true;
            entryPrice = Close[0];
            entryBar = CurrentBar;
            tradeBarsSinceEntry = 0;
            
            // Draw ENTRY indicator
            Draw.TriangleUp(this, "LongEntry_" + CurrentBar, false, 0, Low[0] - (2 * atrValues[0]), Brushes.LimeGreen);
            Draw.Text(this, "LongEntryText_" + CurrentBar, "ENTRY", 0, Low[0] - (3 * atrValues[0]), Brushes.White);
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
            
            // Add fixed tick stop loss protection
            double fixedStopDistance = FixedTickStopSize * TickSize;
            double fixedStopPrice = Close[0] + fixedStopDistance;
            
            // Use the tighter of the two stops
            stopLossPrice = Math.Min(initialStop, fixedStopPrice);
            
            // Check if the stop is too far - adjust risk by reducing position size if needed
            double riskPips = Math.Abs(stopLossPrice - Close[0]);
            int adjustedQuantity = FixedQuantity;
            
            // For higher volatility, consider reducing position size
            double avgAtrValue = SMA(atrValues, 20)[0];
            bool isHighVolatility = atrValues[0] > avgAtrValue * 1.5;
            if (isHighVolatility)
            {
                adjustedQuantity = Math.Max(1, FixedQuantity / 2);
                Print("Reducing position size due to high volatility");
            }
            
            Print("Short Entry at " + Close[0] + " - Stop: " + stopLossPrice +
                  " - Quantity: " + adjustedQuantity + " contracts");
            
            // Calculate adaptive take profit levels
            bool isVolatile = atrValues[0] > avgAtrValue * 1.2;
            
            // In higher volatility, use wider targets
            double tp1RR = isVolatile ? 1.5 : 1.0;
            double tp2RR = isVolatile ? 2.5 : 1.5;
            
            double tp1Distance = (stopLossPrice - Close[0]) * tp1RR;
            double tp2Distance = (stopLossPrice - Close[0]) * tp2RR;
            
            takeProfitPrice1 = Close[0] - tp1Distance;
            takeProfitPrice2 = Close[0] - tp2Distance;
            
            // Enter the position
            EnterShort(adjustedQuantity, reason);
            
            // Update state variables
            inShort = true;
            entryPrice = Close[0];
            entryBar = CurrentBar;
            tradeBarsSinceEntry = 0;
            
            // Draw ENTRY indicator for short
            Draw.TriangleDown(this, "ShortEntry_" + CurrentBar, false, 0, High[0] + (2 * atrValues[0]), Brushes.Crimson);
            Draw.Text(this, "ShortEntryText_" + CurrentBar, "ENTRY", 0, High[0] + (3 * atrValues[0]), Brushes.White);
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
                    ProcessTradeResult(false); // Record loss
                    // Mark exit with text only
                    Draw.Text(this, "ExitText_" + CurrentBar, "EXIT", 0, Low[0], Brushes.Red);
                    return;
                }
                
                // 2. Take profit 1 hit (partial exit)
                if (High[0] >= takeProfitPrice1 && tradeBarsSinceEntry > 1)

                // 3. Take profit 2 hit (exit remaining position)
                if (High[0] >= takeProfitPrice2 && tradeBarsSinceEntry > 1)
                {
                    ExitLong("TP2");
                    ProcessTradeResult(true); // Record win
                    // Keep only exit markers
                    Draw.Text(this, "ExitFinal_" + CurrentBar, "EXIT", 0, takeProfitPrice2, Brushes.Green);
                    return;
                }
                
                // 4. Enhanced trailing stop
                if (UseImprovedTrailingStop && tradeBarsSinceEntry > 3)
                {
                    ApplyImprovedTrailingStop();
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
                    ProcessTradeResult(false); // Record loss
                    // Mark exit with text only
                    Draw.Text(this, "ExitText_" + CurrentBar, "EXIT", 0, High[0], Brushes.Red);
                    return;
                }

                
                // 3. Take profit 2 hit (exit remaining position)
                if (Low[0] <= takeProfitPrice2 && tradeBarsSinceEntry > 1)
                {
                    ExitShort("TP2");
                    ProcessTradeResult(true); // Record win
                    // Keep only exit markers
                    Draw.Text(this, "ExitFinal_" + CurrentBar, "EXIT", 0, takeProfitPrice2, Brushes.Green);
                    return;
                }
                
                // 4. Enhanced trailing stop
                if (UseImprovedTrailingStop && tradeBarsSinceEntry > 3)
                {
                    ApplyImprovedTrailingStop();
                }
            }
        }
        
        private void ApplyImprovedTrailingStop()
        {
            if (inLong)
            {
                // ATR-based trailing stop - more sophisticated than simple fixed stop
                double atrStopDistance = atrValues[0] * 2;
                
                // Use lower lows of previous bars, but weight them by how recent they are
                double trailingStop = double.MaxValue;
                for (int i = 1; i <= 5; i++)
                {
                    if (CurrentBar < i) continue;
                    
                    // More recent bars have more influence
                    double weight = 1.0 - ((i - 1) * 0.15);
                    double barStop = Low[i] - (atrStopDistance * weight);
                    trailingStop = Math.Min(trailingStop, barStop);
                }
                
                // Ensure stop isn't too far from current price
                double maxTrailDistance = Close[0] - entryPrice;
                if (UseFixedTickStop)
                {
                    maxTrailDistance = Math.Min(maxTrailDistance, FixedTickStopSize * TickSize);
                }
                
                if (Close[0] - trailingStop > maxTrailDistance)
                {
                    trailingStop = Close[0] - maxTrailDistance;
                }
                
                // Only move stop loss upward, never downward
                if (trailingStop > stopLossPrice && trailingStop < Close[0])
                {
                    stopLossPrice = trailingStop;
                    Print("Trailing stop updated to " + stopLossPrice);
                }
            }
            else if (inShort) 
            {
                // ATR-based trailing stop
                double atrStopDistance = atrValues[0] * 2;
                
                // Use higher highs of previous bars, but weight them by how recent they are
                double trailingStop = double.MinValue;
                for (int i = 1; i <= 5; i++)
                {
                    if (CurrentBar < i) continue;
                    
                    // More recent bars have more influence
                    double weight = 1.0 - ((i - 1) * 0.15);
                    double barStop = High[i] + (atrStopDistance * weight);
                    trailingStop = Math.Max(trailingStop, barStop);
                }
                
                // Ensure stop isn't too far from current price
                double maxTrailDistance = entryPrice - Close[0];
                if (UseFixedTickStop)
                {
                    maxTrailDistance = Math.Min(maxTrailDistance, FixedTickStopSize * TickSize);
                }
                
                if (trailingStop - Close[0] > maxTrailDistance)
                {
                    trailingStop = Close[0] + maxTrailDistance;
                }
                
                // Only move stop loss downward, never upward
                if (trailingStop < stopLossPrice && trailingStop > Close[0])
                {
                    stopLossPrice = trailingStop;
                    Print("Trailing stop updated to " + stopLossPrice);
                }
            }
        }
        
        private void ProcessTradeResult(bool isWin)
        {
            // Update consecutive losses counter and handle timeout if needed
            if (!isWin)
            {
                consecutiveLosses++;
                lastTradeProfitable = false;
                
                if (consecutiveLosses >= ConsecutiveLossesAllowed)
                {
                    // Take a break from trading for the next hour
                    tradeBreakEndTime = DateTime.Now.AddHours(1);
                    Print("Taking a break after " + consecutiveLosses + " consecutive losses until " + 
                          tradeBreakEndTime.ToString("HH:mm:ss"));
                }
            }
            else
            {
                consecutiveLosses = 0;
                lastTradeProfitable = true;
            }
            
            ResetPositionState();
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
            Print("Position state reset");
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
        
        #endregion
        
        #region Properties
        
        [NinjaScriptProperty]
        [Display(Name = "Lookback Period", Description = "Number of bars to look back for swing points", Order = 1, GroupName = "Strategy Parameters")]
        [Range(10, 200)]
        public int LookbackPeriod
        {
            get { return lookbackPeriod; }
            set { lookbackPeriod = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "ATR Multiplier", Description = "Multiplier for ATR to set stop buffer", Order = 2, GroupName = "Strategy Parameters")]
        [Range(0.1, 3.0)]
        public double ATRMultiplier
        {
            get { return atrMultiplier; }
            set { atrMultiplier = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Bars Required for High", Description = "Number of bars to confirm a swing high", Order = 3, GroupName = "Strategy Parameters")]
        [Range(1, 10)]
        public int BarsRequiredToTradeHigh
        {
            get { return barsRequiredToTradeHigh; }
            set { barsRequiredToTradeHigh = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Bars Required for Low", Description = "Number of bars to confirm a swing low", Order = 4, GroupName = "Strategy Parameters")]
        [Range(1, 10)]
        public int BarsRequiredToTradeLow
        {
            get { return barsRequiredToTradeLow; }
            set { barsRequiredToTradeLow = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Fixed Tick Stop", Description = "Enable fixed tick stop loss", Order = 5, GroupName = "Risk Management")]
        public bool UseFixedTickStop
        {
            get { return useFixedTickStop; }
            set { useFixedTickStop = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Fixed Tick Stop Size", Description = "Maximum stop loss distance in ticks", Order = 6, GroupName = "Risk Management")]
        [Range(5, 200)]
        public int FixedTickStopSize
        {
            get { return fixedTickStopSize; }
            set { fixedTickStopSize = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Use LiqSweepBosOb Entry", Description = "Enable liquidity sweep + BOS + OB entry setup", Order = 7, GroupName = "Entry Methods")]
        public bool UseLiqSweepBosObEntry
        {
            get { return useLiqSweepBosObEntry; }
            set { useLiqSweepBosObEntry = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Fixed Quantity", Description = "Fixed number of contracts to trade", Order = 8, GroupName = "Position Sizing")]
        [Range(1, 100)]
        public int FixedQuantity
        {
            get { return fixedQuantity; }
            set { fixedQuantity = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Enable Time Filter", Description = "Only trade during optimal market hours", Order = 9, GroupName = "Optimization Filters")]
        public bool EnableTimeFilter
        {
            get { return enableTimeFilter; }
            set { enableTimeFilter = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Enable Volume Filter", Description = "Only trade when volume is above average", Order = 10, GroupName = "Optimization Filters")]
        public bool EnableVolumeFilter
        {
            get { return enableVolumeFilter; }
            set { enableVolumeFilter = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Enable Trend Filter", Description = "Only trade in the direction of the trend based on EMAs and ADX", Order = 11, GroupName = "Optimization Filters")]
        public bool EnableTrendFilter
        {
            get { return enableTrendFilter; }
            set { enableTrendFilter = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Enable Quality Filter", Description = "Only take high-quality trade setups based on score", Order = 12, GroupName = "Optimization Filters")]
        public bool EnableQualityFilter
        {
            get { return enableQualityFilter; }
            set { enableQualityFilter = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Max Daily Trades", Description = "Maximum number of trades to take per day", Order = 13, GroupName = "Risk Management")]
        [Range(1, 10)]
        public int MaxDailyTrades
        {
            get { return maxDailyTrades; }
            set { maxDailyTrades = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Quality Score Threshold", Description = "Minimum quality score required to take a trade (0-100)", Order = 14, GroupName = "Optimization Filters")]
        [Range(0, 100)]
        public int QualityScoreThreshold
        {
            get { return qualityScoreThreshold; }
            set { qualityScoreThreshold = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Use Improved Trailing Stop", Description = "Use enhanced trailing stop logic", Order = 15, GroupName = "Risk Management")]
        public bool UseImprovedTrailingStop
        {
            get { return useImprovedTrailingStop; }
            set { useImprovedTrailingStop = value; }
        }
        
        [NinjaScriptProperty]
        [Display(Name = "Consecutive Losses Allowed", Description = "Number of consecutive losses before taking a break", Order = 16, GroupName = "Risk Management")]
        [Range(1, 5)]
        public int ConsecutiveLossesAllowed
        {
            get { return consecutiveLossesAllowed; }
            set { consecutiveLossesAllowed = value; }
        }
        
        #endregion
    }
}