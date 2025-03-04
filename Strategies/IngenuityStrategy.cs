#region Using declarations
using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.NinjaScript;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public class IngenuityStrategy : Strategy
    {
        // --- Strategy parameters ---
        [NinjaScriptProperty]
        public double WeightOB { get; set; }

        [NinjaScriptProperty]
        public double WeightLIQ { get; set; }

        [NinjaScriptProperty]
        public double WeightBOS { get; set; }

        [NinjaScriptProperty]
        public double Threshold { get; set; }

        // Indicator references
        private OrderBlocksIndicator obIndicator;
        private LiquiditySweepIndicator lsIndicator;
        private BreakOfStructureIndicator bosIndicator;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "IngenuityStrategy";
                Description = "Combines OB, LIQ sweeps, and BOS signals with a threshold-based entry.";
                Calculate = Calculate.OnBarClose;
                EntriesPerDirection = 1;
                EntryHandling = EntryHandling.AllEntries;
                IsExitOnSessionCloseStrategy = true;
                IncludeCommission = true;
                TraceOrders = false;

                // Default parameter values
                WeightOB = 30;
                WeightLIQ = 30;
                WeightBOS = 40;
                Threshold = 60;
            }
            else if (State == State.DataLoaded)
            {
                // ✅ Correctly instantiate indicators
                obIndicator = OrderBlocksIndicator(Input);
                lsIndicator = LiquiditySweepIndicator(Input, 10, 24);
                bosIndicator = BreakOfStructureIndicator(Input, 4);

                // ✅ Add indicators to chart (optional)
                AddChartIndicator(obIndicator);
                AddChartIndicator(lsIndicator);
                AddChartIndicator(bosIndicator);
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < 2) 
            {
                return; // ✅ Fix misplaced return statement
            }

            // ✅ Ensure indicators are not null before using them
            if (obIndicator == null || lsIndicator == null || bosIndicator == null)
            {
                Print("Indicators not ready yet");
                return;
            }

            // ✅ Ensure indicator values are valid before calculation
            double obVal = (obIndicator != null && CurrentBar >= 2) ? obIndicator[0] : 0;
            double liqVal = (lsIndicator != null && CurrentBar >= 2) ? lsIndicator.LiquiditySignal[0] : 0;
            double bosVal = (bosIndicator != null && CurrentBar >= 2) ? bosIndicator.BOSSignal[0] : 0;

            // ✅ Debugging: Print indicator values
            Print($"Bar: {CurrentBar}, OB: {obVal}, LIQ: {liqVal}, BOS: {bosVal}");

            // ✅ Weighted sum calculation
            double score = (WeightOB * obVal) + (WeightLIQ * liqVal) + (WeightBOS * bosVal);

            // ✅ Debugging: Print score
            Print($"Score: {score}, Threshold: {Threshold}");

            // ✅ Stop-Loss and Take-Profit (Placed Before Entries)
            SetStopLoss(CalculationMode.Ticks, 50);
            SetProfitTarget(CalculationMode.Ticks, 150);

            // ✅ Enter Trade if No Open Position
            if (score >= Threshold && Position.MarketPosition == MarketPosition.Flat)
            {
                Print("Entering Long Position");
                EnterLong();
            }
            else if (score <= -Threshold && Position.MarketPosition == MarketPosition.Flat)
            {
                Print("Entering Short Position");
                EnterShort();
            }
        }
    }
}
