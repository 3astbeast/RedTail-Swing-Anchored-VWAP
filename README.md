<p align="center">
  <img src="https://avatars.githubusercontent.com/u/209633456?v=4" width="160" alt="RedTail Indicators Logo"/>
</p>

<h1 align="center">RedTail Swing Anchored VWAP</h1>

<p align="center">
  <b>An adaptive swing-anchored VWAP indicator for NinjaTrader 8.</b><br>
  Automatically re-anchors VWAP from detected swing pivots using EWMA smoothing with optional ATR-adaptive reaction speed.
</p>

<p align="center">
  <a href="https://buymeacoffee.com/dmwyzlxstj">
    <img src="https://img.shields.io/badge/☕_Buy_Me_a_Coffee-FFDD00?style=flat-square&logo=buy-me-a-coffee&logoColor=black" alt="Buy Me a Coffee"/>
  </a>
</p>

---

## Overview

RedTail Swing Anchored VWAP detects swing highs and lows on your chart, then anchors a VWAP from each pivot point using EWMA (Exponentially Weighted Moving Average) smoothing. Unlike traditional cumulative VWAP which weights all bars equally from the anchor, EWMA applies an exponential decay — recent bars carry more weight, producing a smoother, more responsive VWAP that adapts to changing conditions while still respecting the anchor origin.

---

## How It Works

The indicator runs a swing detection engine using a configurable lookback period. When the most recent swing high occurs after the most recent swing low, the trend direction is bullish; when the most recent swing low is newer, the direction is bearish.

When the direction changes — signaling a new swing pivot — the VWAP re-anchors from the pivot bar and walks forward to the current bar, recalculating the EWMA at each step. The VWAP line changes color to reflect the current direction: bullish when anchored from a swing low, bearish when anchored from a swing high.

On continuation bars (no direction change), the EWMA simply updates with the current bar's HLC3 and volume.

---

## Swing Detection

**Swing Period** — The number of bars used to detect swing highs and lows (default: 50). A bar's high must be the highest high in the lookback to qualify as a swing high; likewise for swing lows. Larger values find bigger, more significant swings; smaller values are more sensitive.

---

## Adaptive Price Tracking

The EWMA's reaction speed is controlled by the Adaptive Price Tracking (APT) parameter, which acts as the half-life of the exponential decay.

**Adaptive Price Tracking** — Controls how quickly the VWAP reacts to new price/volume data (default: 20). Lower values produce a tighter, faster-reacting VWAP. Higher values produce a smoother, slower VWAP. This is the base half-life in bars.

**Adapt APT by ATR** — When enabled, the APT value automatically adjusts based on current volatility. The indicator computes a 50-bar ATR and compares it to its own RMA-smoothed average. When volatility is elevated (ATR > smoothed ATR), the APT decreases — making the VWAP tighter and more responsive. When volatility is low, the APT increases for a smoother line. Disabled by default.

**Volatility Bias** — Controls how aggressively volatility influences the APT adjustment (default: 10). Higher values amplify the effect of volatility changes. The adjusted APT is clamped between 5 and 300 to prevent extreme behavior.

---

## Historical VWAPs

**Show Historical VWAPs** — When enabled, all previous VWAP segments from prior swing anchors are displayed on the chart, creating a visual trail of swing-to-swing VWAPs. When disabled (default), only the current active segment is shown.

---

## Visual Settings

- **Bull VWAP Color** — Color for the VWAP line during bullish segments. Default: Lime.
- **Bear VWAP Color** — Color for the VWAP line during bearish segments. Default: Red.
- **Line Width** — 1 to 10 pixels. Default: 2.
- **Opacity** — 0% (fully transparent) to 100% (fully opaque). Default: 100%.
- **Line Style** — Solid, Dash, Dot, DashDot, or DashDotDot. Default: Solid.

---

## Plot Output

The current VWAP value is exposed as a plot output ("VWAP"), making it available in the data box, crosshair readout, and for use by other indicators or strategies.

---

## Installation

1. Download the `.cs` file from this repository
2. Copy the `.cs` to `Documents\NinjaTrader 8\bin\Custom\Indicators`
3. Open NinjaTrader (if not already open)
4. In Control Center, go to **New → NinjaScript Editor**
5. Expand the Indicator tree, find your new indicator, double-click to open it
6. At the top of the Editor window, click the **Compile** button
7. That's it!

---

## Part of the RedTail Indicators Suite

This indicator is part of the [RedTail Indicators](https://github.com/3astbeast/RedTailIndicators) collection — free NinjaTrader 8 tools built for futures traders who demand precision.

---

<p align="center">
  <a href="https://buymeacoffee.com/dmwyzlxstj">
    <img src="https://img.shields.io/badge/☕_Buy_Me_a_Coffee-Support_My_Work-FFDD00?style=for-the-badge&logo=buy-me-a-coffee&logoColor=black" alt="Buy Me a Coffee"/>
  </a>
</p>
