#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows.Media;
using System.Xml.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Core;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.NinjaScript;
using SharpDX;
#endregion

namespace NinjaTrader.NinjaScript.Indicators.RedTail
{
    /// <summary>
    /// RedTail Swing Anchored VWAP
    /// Anchors VWAP to detected swing highs/lows with EWMA smoothing and
    /// optional ATR-adaptive half-life. Labels swings as HH/HL/LH/LL.
    /// </summary>
    public class RedTailSwingAnchoredVWAP : Indicator
    {
        #region Private Fields

        // Direction tracking
        private int dir;
        private int prevDir;

        // Swing tracking
        private double ph;
        private double pl;
        private int phBar;
        private int plBar;

        // EWMA VWAP state
        private double ewmaP;
        private double ewmaVol;

        // Rendering - historical segments + active segment
        private List<VwapSegment> historicalSegments;
        private List<VwapPoint> activePoints;

        // Track active segment direction for coloring
        private int activeSegmentDir;

        // ATR fields
        private double atrValue;
        private double atrRma;
        private bool atrReady;

        // Cached SharpDX resources
        private SharpDX.Direct2D1.Brush dxBullBrush;
        private SharpDX.Direct2D1.Brush dxBearBrush;
        private SharpDX.Direct2D1.StrokeStyle dxStrokeStyle;
        private bool brushesValid;

        #endregion

        #region Structs / Classes

        private struct VwapPoint
        {
            public int BarIndex;
            public double Value;

            public VwapPoint(int bar, double val)
            {
                BarIndex = bar;
                Value = val;
            }
        }

        private class VwapSegment
        {
            public List<VwapPoint> Points;
            public int Direction; // 1 = bull, -1 = bear

            public VwapSegment(List<VwapPoint> points, int direction)
            {
                Points = new List<VwapPoint>(points);
                Direction = direction;
            }
        }

        #endregion

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Description                 = @"RedTail Swing Anchored VWAP - anchors VWAP to swing pivots with adaptive EWMA smoothing.";
                Name                        = "RedTailSwingAnchoredVWAP";
                Calculate                   = Calculate.OnBarClose;
                IsOverlay                   = true;
                DisplayInDataBox            = true;
                DrawOnPricePanel            = true;
                PaintPriceMarkers           = false;
                IsSuspendedWhileInactive    = true;
                BarsRequiredToPlot          = 50;

                // Parameters
                SwingPeriod             = 50;
                AdaptivePriceTracking   = 20.0;
                AdaptAPTByATR           = false;
                VolatilityBias          = 10.0;
                ShowHistoricalVWAPs     = false;

                // Style
                BullColor           = Brushes.Lime;
                BearColor           = Brushes.Red;
                VwapLineWidth       = 2;
                VwapOpacity         = 100;
                VwapLineStyle       = DashStyleHelper.Solid;

                AddPlot(new Stroke(Brushes.Transparent, 2), PlotStyle.Line, "VWAP");
            }
            else if (State == State.Configure)
            {
            }
            else if (State == State.DataLoaded)
            {
                historicalSegments = new List<VwapSegment>();
                activePoints = new List<VwapPoint>();
                activeSegmentDir = 0;
                dir = 0;
                prevDir = 0;
                ph = double.MinValue;
                pl = double.MaxValue;
                phBar = 0;
                plBar = 0;
                ewmaP = 0;
                ewmaVol = 0;
                atrValue = 0;
                atrRma = 0;
                atrReady = false;
                brushesValid = false;
            }
            else if (State == State.Terminated)
            {
                DisposeResources();
            }
        }

        protected override void OnBarUpdate()
        {
            if (CurrentBar < SwingPeriod)
                return;

            // --- ATR Calculation ---
            ComputeATR();

            // --- Swing Detection ---
            bool isSwingHigh = IsHighest(High, SwingPeriod);
            bool isSwingLow  = IsLowest(Low, SwingPeriod);

            if (isSwingHigh)
            {
                ph = High[0];
                phBar = CurrentBar;
            }
            if (isSwingLow)
            {
                pl = Low[0];
                plBar = CurrentBar;
            }

            prevDir = dir;
            dir = phBar > plBar ? 1 : -1;

            // --- Adaptive Price Tracking ---
            double apt = ComputeAPT();

            // --- Direction Change: Re-anchor VWAP ---
            if (dir != prevDir && prevDir != 0)
            {
                // Archive the current active segment as historical
                if (activePoints.Count >= 2)
                {
                    historicalSegments.Add(new VwapSegment(activePoints, activeSegmentDir));
                }

                // Anchor point
                int anchorBar      = dir > 0 ? plBar : phBar;
                int barsBack       = CurrentBar - anchorBar;

                // Re-initialize EWMA from anchor bar
                if (barsBack >= 0 && barsBack < CurrentBar)
                {
                    double anchorVol  = Volume.GetValueAt(anchorBar);
                    double anchorHLC3 = (High.GetValueAt(anchorBar) + Low.GetValueAt(anchorBar) + Close.GetValueAt(anchorBar)) / 3.0;

                    ewmaP   = anchorHLC3 * anchorVol;
                    ewmaVol = anchorVol;

                    activePoints.Clear();
                    activeSegmentDir = dir;

                    // Walk forward from anchor to current bar
                    for (int i = barsBack; i >= 0; i--)
                    {
                        int barIdx = CurrentBar - i;
                        double alpha = AlphaFromAPT(apt);

                        double barHLC3 = (High.GetValueAt(barIdx) + Low.GetValueAt(barIdx) + Close.GetValueAt(barIdx)) / 3.0;
                        double barVol  = Volume.GetValueAt(barIdx);

                        double pxv = barHLC3 * barVol;

                        ewmaP   = (1.0 - alpha) * ewmaP   + alpha * pxv;
                        ewmaVol = (1.0 - alpha) * ewmaVol + alpha * barVol;

                        double vwapVal = ewmaVol > 0 ? ewmaP / ewmaVol : barHLC3;
                        activePoints.Add(new VwapPoint(barIdx, vwapVal));
                    }
                }
            }
            else
            {
                // Continuation: update EWMA with current bar
                if (activeSegmentDir == 0)
                    activeSegmentDir = dir;

                double alpha = AlphaFromAPT(apt);
                double hlc3  = (High[0] + Low[0] + Close[0]) / 3.0;
                double vol   = Volume[0];
                double pxv   = hlc3 * vol;

                ewmaP   = (1.0 - alpha) * ewmaP   + alpha * pxv;
                ewmaVol = (1.0 - alpha) * ewmaVol + alpha * vol;

                double vwapVal = ewmaVol > 0 ? ewmaP / ewmaVol : hlc3;
                activePoints.Add(new VwapPoint(CurrentBar, vwapVal));
            }

            // Set plot value for data box / crosshair
            if (activePoints.Count > 0)
            {
                Values[0][0] = activePoints[activePoints.Count - 1].Value;
            }
        }

        #region Helpers

        private void ComputeATR()
        {
            int atrLen = 50;
            double tr = CurrentBar == 0
                ? High[0] - Low[0]
                : Math.Max(High[0] - Low[0], Math.Max(Math.Abs(High[0] - Close[1]), Math.Abs(Low[0] - Close[1])));

            if (!atrReady)
            {
                if (CurrentBar < atrLen)
                {
                    atrValue = tr;
                }
                else if (CurrentBar == atrLen)
                {
                    double sum = 0;
                    for (int i = 0; i < atrLen; i++)
                    {
                        double h = High.GetValueAt(CurrentBar - i);
                        double l = Low.GetValueAt(CurrentBar - i);
                        double c = i < CurrentBar ? Close.GetValueAt(CurrentBar - i - 1) : 0;
                        double tri = i == CurrentBar
                            ? h - l
                            : Math.Max(h - l, Math.Max(Math.Abs(h - c), Math.Abs(l - c)));
                        sum += tri;
                    }
                    atrValue = sum / atrLen;
                    atrRma = atrValue;
                    atrReady = true;
                }
            }
            else
            {
                atrValue = (atrValue * (atrLen - 1) + tr) / atrLen;
                atrRma   = (atrRma   * (atrLen - 1) + atrValue) / atrLen;
            }
        }

        private double ComputeAPT()
        {
            double baseAPT = AdaptivePriceTracking;

            if (!AdaptAPTByATR || !atrReady || atrRma <= 0)
                return baseAPT;

            double ratio = atrValue / atrRma;
            double aptRaw = baseAPT / Math.Pow(ratio, VolatilityBias);
            double aptClamped = Math.Max(5.0, Math.Min(300.0, aptRaw));
            return Math.Round(aptClamped);
        }

        private double AlphaFromAPT(double apt)
        {
            double decay = Math.Exp(-Math.Log(2.0) / Math.Max(1.0, apt));
            return 1.0 - decay;
        }

        private bool IsHighest(ISeries<double> series, int period)
        {
            double val = series[0];
            for (int i = 1; i < period; i++)
            {
                if (CurrentBar - i < 0) break;
                if (series[i] > val)
                    return false;
            }
            return true;
        }

        private bool IsLowest(ISeries<double> series, int period)
        {
            double val = series[0];
            for (int i = 1; i < period; i++)
            {
                if (CurrentBar - i < 0) break;
                if (series[i] < val)
                    return false;
            }
            return true;
        }

        private void DrawSegment(SharpDX.Direct2D1.RenderTarget renderTarget, ChartControl chartControl, ChartScale chartScale,
            List<VwapPoint> points, int segDir, float lineWidth)
        {
            if (points == null || points.Count < 2)
                return;

            SharpDX.Direct2D1.Brush lineBrush = segDir > 0 ? dxBullBrush : dxBearBrush;

            for (int i = 1; i < points.Count; i++)
            {
                var p0 = points[i - 1];
                var p1 = points[i];

                float x0 = chartControl.GetXByBarIndex(ChartBars, p0.BarIndex);
                float x1 = chartControl.GetXByBarIndex(ChartBars, p1.BarIndex);
                float y0 = chartScale.GetYByValue(p0.Value);
                float y1 = chartScale.GetYByValue(p1.Value);

                if (float.IsNaN(x0) || float.IsNaN(x1) || float.IsNaN(y0) || float.IsNaN(y1))
                    continue;
                if (float.IsInfinity(x0) || float.IsInfinity(x1) || float.IsInfinity(y0) || float.IsInfinity(y1))
                    continue;

                renderTarget.DrawLine(
                    new SharpDX.Vector2(x0, y0),
                    new SharpDX.Vector2(x1, y1),
                    lineBrush,
                    lineWidth,
                    dxStrokeStyle);
            }
        }

        private void DisposeResources()
        {
            if (dxBullBrush != null) { dxBullBrush.Dispose(); dxBullBrush = null; }
            if (dxBearBrush != null) { dxBearBrush.Dispose(); dxBearBrush = null; }
            if (dxStrokeStyle != null) { dxStrokeStyle.Dispose(); dxStrokeStyle = null; }
            brushesValid = false;
        }

        #endregion

        #region SharpDX Rendering

        protected override void OnRender(ChartControl chartControl, ChartScale chartScale)
        {
            base.OnRender(chartControl, chartScale);

            var renderTarget = RenderTarget;
            if (renderTarget == null)
                return;

            // Create/recreate DX brushes
            if (!brushesValid)
            {
                DisposeResources();

                float opacity = Math.Max(0f, Math.Min(1f, VwapOpacity / 100f));

                System.Windows.Media.Color bullMediaColor = ((System.Windows.Media.SolidColorBrush)BullColor).Color;
                System.Windows.Media.Color bearMediaColor = ((System.Windows.Media.SolidColorBrush)BearColor).Color;

                dxBullBrush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget,
                    new SharpDX.Color4(bullMediaColor.R / 255f, bullMediaColor.G / 255f, bullMediaColor.B / 255f, opacity));
                dxBearBrush = new SharpDX.Direct2D1.SolidColorBrush(renderTarget,
                    new SharpDX.Color4(bearMediaColor.R / 255f, bearMediaColor.G / 255f, bearMediaColor.B / 255f, opacity));

                // Map DashStyleHelper to SharpDX DashStyle
                SharpDX.Direct2D1.DashStyle dxDash;
                switch (VwapLineStyle)
                {
                    case DashStyleHelper.Dash:       dxDash = SharpDX.Direct2D1.DashStyle.Dash; break;
                    case DashStyleHelper.DashDot:     dxDash = SharpDX.Direct2D1.DashStyle.DashDot; break;
                    case DashStyleHelper.DashDotDot:  dxDash = SharpDX.Direct2D1.DashStyle.DashDotDot; break;
                    case DashStyleHelper.Dot:         dxDash = SharpDX.Direct2D1.DashStyle.Dot; break;
                    default:                          dxDash = SharpDX.Direct2D1.DashStyle.Solid; break;
                }

                dxStrokeStyle = new SharpDX.Direct2D1.StrokeStyle(renderTarget.Factory,
                    new SharpDX.Direct2D1.StrokeStyleProperties
                    {
                        DashStyle = dxDash,
                        LineJoin  = SharpDX.Direct2D1.LineJoin.Round
                    });

                brushesValid = true;
            }

            float lineWidth = VwapLineWidth;

            // Draw historical VWAP segments (if enabled)
            if (ShowHistoricalVWAPs && historicalSegments != null)
            {
                for (int s = 0; s < historicalSegments.Count; s++)
                {
                    DrawSegment(renderTarget, chartControl, chartScale,
                        historicalSegments[s].Points, historicalSegments[s].Direction, lineWidth);
                }
            }

            // Always draw the active (current) segment
            if (activePoints != null && activePoints.Count >= 2)
            {
                DrawSegment(renderTarget, chartControl, chartScale,
                    activePoints, activeSegmentDir, lineWidth);
            }
        }

        public override void OnRenderTargetChanged()
        {
            DisposeResources();
        }

        #endregion

        #region Properties

        [NinjaScriptProperty]
        [Range(2, int.MaxValue)]
        [Display(Name = "Swing Period", Description = "Number of bars used to detect swing highs/lows. Larger values find bigger swings.", Order = 1, GroupName = "Swing Points")]
        public int SwingPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1.0, double.MaxValue)]
        [Display(Name = "Adaptive Price Tracking", Description = "Controls VWAP reaction speed. Lower = faster/tighter, Higher = smoother/slower.", Order = 2, GroupName = "Swing Points")]
        public double AdaptivePriceTracking { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Adapt APT by ATR", Description = "When enabled, VWAP reaction speed adapts automatically based on market volatility.", Order = 3, GroupName = "Swing Points")]
        public bool AdaptAPTByATR { get; set; }

        [NinjaScriptProperty]
        [Range(0.1, double.MaxValue)]
        [Display(Name = "Volatility Bias", Description = "Controls how strongly volatility influences VWAP reaction speed.", Order = 4, GroupName = "Swing Points")]
        public double VolatilityBias { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Show Historical VWAPs", Description = "When enabled, displays all previous VWAP segments from prior swing anchors. When disabled, only the current active VWAP is shown.", Order = 5, GroupName = "Swing Points")]
        public bool ShowHistoricalVWAPs { get; set; }

        [XmlIgnore]
        [Display(Name = "Bull VWAP Color", Description = "Color for VWAP line in uptrend.", Order = 1, GroupName = "Style")]
        public Brush BullColor { get; set; }

        [Browsable(false)]
        public string BullColorSerializable
        {
            get { return Serialize.BrushToString(BullColor); }
            set { BullColor = Serialize.StringToBrush(value); }
        }

        [XmlIgnore]
        [Display(Name = "Bear VWAP Color", Description = "Color for VWAP line in downtrend.", Order = 2, GroupName = "Style")]
        public Brush BearColor { get; set; }

        [Browsable(false)]
        public string BearColorSerializable
        {
            get { return Serialize.BrushToString(BearColor); }
            set { BearColor = Serialize.StringToBrush(value); }
        }

        [NinjaScriptProperty]
        [Range(1, 10)]
        [Display(Name = "Line Width", Description = "Thickness of the VWAP line.", Order = 3, GroupName = "Style")]
        public int VwapLineWidth { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Opacity %", Description = "Opacity of the VWAP lines. 0 = fully transparent, 100 = fully opaque.", Order = 4, GroupName = "Style")]
        public int VwapOpacity { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Line Style", Description = "Dash style for the VWAP lines (Solid, Dash, Dot, DashDot, DashDotDot).", Order = 5, GroupName = "Style")]
        public DashStyleHelper VwapLineStyle { get; set; }

        #endregion
    }
}

#region NinjaScript generated code. Neither change nor remove.

namespace NinjaTrader.NinjaScript.Indicators
{
	public partial class Indicator : NinjaTrader.Gui.NinjaScript.IndicatorRenderBase
	{
		private RedTail.RedTailSwingAnchoredVWAP[] cacheRedTailSwingAnchoredVWAP;
		public RedTail.RedTailSwingAnchoredVWAP RedTailSwingAnchoredVWAP(int swingPeriod, double adaptivePriceTracking, bool adaptAPTByATR, double volatilityBias, bool showHistoricalVWAPs, int vwapLineWidth, int vwapOpacity, DashStyleHelper vwapLineStyle)
		{
			return RedTailSwingAnchoredVWAP(Input, swingPeriod, adaptivePriceTracking, adaptAPTByATR, volatilityBias, showHistoricalVWAPs, vwapLineWidth, vwapOpacity, vwapLineStyle);
		}

		public RedTail.RedTailSwingAnchoredVWAP RedTailSwingAnchoredVWAP(ISeries<double> input, int swingPeriod, double adaptivePriceTracking, bool adaptAPTByATR, double volatilityBias, bool showHistoricalVWAPs, int vwapLineWidth, int vwapOpacity, DashStyleHelper vwapLineStyle)
		{
			if (cacheRedTailSwingAnchoredVWAP != null)
				for (int idx = 0; idx < cacheRedTailSwingAnchoredVWAP.Length; idx++)
					if (cacheRedTailSwingAnchoredVWAP[idx] != null && cacheRedTailSwingAnchoredVWAP[idx].SwingPeriod == swingPeriod && cacheRedTailSwingAnchoredVWAP[idx].AdaptivePriceTracking == adaptivePriceTracking && cacheRedTailSwingAnchoredVWAP[idx].AdaptAPTByATR == adaptAPTByATR && cacheRedTailSwingAnchoredVWAP[idx].VolatilityBias == volatilityBias && cacheRedTailSwingAnchoredVWAP[idx].ShowHistoricalVWAPs == showHistoricalVWAPs && cacheRedTailSwingAnchoredVWAP[idx].VwapLineWidth == vwapLineWidth && cacheRedTailSwingAnchoredVWAP[idx].VwapOpacity == vwapOpacity && cacheRedTailSwingAnchoredVWAP[idx].VwapLineStyle == vwapLineStyle && cacheRedTailSwingAnchoredVWAP[idx].EqualsInput(input))
						return cacheRedTailSwingAnchoredVWAP[idx];
			return CacheIndicator<RedTail.RedTailSwingAnchoredVWAP>(new RedTail.RedTailSwingAnchoredVWAP(){ SwingPeriod = swingPeriod, AdaptivePriceTracking = adaptivePriceTracking, AdaptAPTByATR = adaptAPTByATR, VolatilityBias = volatilityBias, ShowHistoricalVWAPs = showHistoricalVWAPs, VwapLineWidth = vwapLineWidth, VwapOpacity = vwapOpacity, VwapLineStyle = vwapLineStyle }, input, ref cacheRedTailSwingAnchoredVWAP);
		}
	}
}

namespace NinjaTrader.NinjaScript.MarketAnalyzerColumns
{
	public partial class MarketAnalyzerColumn : MarketAnalyzerColumnBase
	{
		public Indicators.RedTail.RedTailSwingAnchoredVWAP RedTailSwingAnchoredVWAP(int swingPeriod, double adaptivePriceTracking, bool adaptAPTByATR, double volatilityBias, bool showHistoricalVWAPs, int vwapLineWidth, int vwapOpacity, DashStyleHelper vwapLineStyle)
		{
			return indicator.RedTailSwingAnchoredVWAP(Input, swingPeriod, adaptivePriceTracking, adaptAPTByATR, volatilityBias, showHistoricalVWAPs, vwapLineWidth, vwapOpacity, vwapLineStyle);
		}

		public Indicators.RedTail.RedTailSwingAnchoredVWAP RedTailSwingAnchoredVWAP(ISeries<double> input , int swingPeriod, double adaptivePriceTracking, bool adaptAPTByATR, double volatilityBias, bool showHistoricalVWAPs, int vwapLineWidth, int vwapOpacity, DashStyleHelper vwapLineStyle)
		{
			return indicator.RedTailSwingAnchoredVWAP(input, swingPeriod, adaptivePriceTracking, adaptAPTByATR, volatilityBias, showHistoricalVWAPs, vwapLineWidth, vwapOpacity, vwapLineStyle);
		}
	}
}

namespace NinjaTrader.NinjaScript.Strategies
{
	public partial class Strategy : NinjaTrader.Gui.NinjaScript.StrategyRenderBase
	{
		public Indicators.RedTail.RedTailSwingAnchoredVWAP RedTailSwingAnchoredVWAP(int swingPeriod, double adaptivePriceTracking, bool adaptAPTByATR, double volatilityBias, bool showHistoricalVWAPs, int vwapLineWidth, int vwapOpacity, DashStyleHelper vwapLineStyle)
		{
			return indicator.RedTailSwingAnchoredVWAP(Input, swingPeriod, adaptivePriceTracking, adaptAPTByATR, volatilityBias, showHistoricalVWAPs, vwapLineWidth, vwapOpacity, vwapLineStyle);
		}

		public Indicators.RedTail.RedTailSwingAnchoredVWAP RedTailSwingAnchoredVWAP(ISeries<double> input , int swingPeriod, double adaptivePriceTracking, bool adaptAPTByATR, double volatilityBias, bool showHistoricalVWAPs, int vwapLineWidth, int vwapOpacity, DashStyleHelper vwapLineStyle)
		{
			return indicator.RedTailSwingAnchoredVWAP(input, swingPeriod, adaptivePriceTracking, adaptAPTByATR, volatilityBias, showHistoricalVWAPs, vwapLineWidth, vwapOpacity, vwapLineStyle);
		}
	}
}

#endregion
