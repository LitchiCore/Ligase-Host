# Ligase cross-platform color tokens v1

Status: proposed for Host and Android review. Do not implement until both sides
record `ACCEPT`.

This contract defines semantic color roles shared by Ligase Host and Ligase
Android. WinUI and Material 3 retain their native hover, pressed, ripple,
elevation, and disabled-state behavior. Semantic hierarchy and accessibility
must match; pixels do not need to match.

## Current Host audit

The current Host theme has these overlapping sources:

- `Themes/Colors.xaml` repeats the complete light palette in both `Default` and
  `Light`.
- `MainWindow.xaml` defines a second light/dark resource dictionary for
  NavigationView pane and selected colors, overriding the central dictionary.
- Existing resources mix semantic roles (`Success`) with component or page
  roles (`GameTile`, `Card`, `Pane`, `Hero`) and raw hue names (`Cyan`).
- Success exists, but warning, error/danger, disabled content, and focus-ring
  roles do not.
- Text often uses WinUI system brushes while branded text uses Ligase brushes,
  so the effective text palette has two authorities.
- `StreamMonitorPage.xaml` contains raw near-black viewport, overlay, and
  placeholder colors. Those represent captured-video chrome, not app surfaces;
  they remain an explicitly documented debt until the monitor component gets
  its own media-overlay tokens.
- `Foreground="White"` is used on the logo and video status overlay. It is safe
  only when the paired fill maintains the measured contrast.

The implementation must remove the MainWindow NavigationView override and map
all Ligase pages to one ThemeResource source. It must not modify the upstream
Apollo Web UI.

## Frozen machine names and proposed values

Machine names are lower camel case and are not page or component names.

| Token | Light | Dark | Meaning |
|---|---:|---:|---|
| `brandPrimary` | `#6258D9` | `#B8B1FF` | Primary action, active indicator, branded icon or text |
| `brandSecondary` | `#08788B` | `#6ED6E4` | Secondary brand accent; never a generic information status |
| `background` | `#F5F7FC` | `#15171D` | App canvas behind navigation and content |
| `surface` | `#FFFFFF` | `#20232C` | Cards, dialogs, menus and raised content |
| `surfaceVariant` | `#EEF2F8` | `#2A2E39` | Secondary groups, navigation pane and quiet containers |
| `textPrimary` | `#20232C` | `#F5F7FB` | Titles and normal body text |
| `textSecondary` | `#555D6D` | `#B8C0CE` | Supporting text and metadata |
| `border` | `#8B94A5` | `#747D8E` | Meaningful control/card boundary and divider |
| `selected` | `#E7E5FF` | `#35315C` | Selected container background; normal text/icons use `textPrimary`, while `brandPrimary` is limited to the indicator or a key icon |
| `success` | `#13795B` | `#56D19B` | Ready, paired, online, completed |
| `warning` | `#8A4F00` | `#F4B860` | Degraded state or action needed without data loss |
| `errorDanger` | `#B42318` | `#FF7B72` | Error and destructive action; always pair with text/icon/confirmation |
| `disabled` | `#6B7382` | `#8D96A6` | Essential disabled content readability floor; do not dim it again below the contrast requirement |
| `focus` | `#4F46C7` | `#B8B1FF` | Keyboard/gamepad focus ring |

Canonical machine-readable form:

```json
{
  "schemaVersion": 1,
  "light": {
    "brandPrimary": "#6258D9",
    "brandSecondary": "#08788B",
    "background": "#F5F7FC",
    "surface": "#FFFFFF",
    "surfaceVariant": "#EEF2F8",
    "textPrimary": "#20232C",
    "textSecondary": "#555D6D",
    "border": "#8B94A5",
    "selected": "#E7E5FF",
    "success": "#13795B",
    "warning": "#8A4F00",
    "errorDanger": "#B42318",
    "disabled": "#6B7382",
    "focus": "#4F46C7"
  },
  "dark": {
    "brandPrimary": "#B8B1FF",
    "brandSecondary": "#6ED6E4",
    "background": "#15171D",
    "surface": "#20232C",
    "surfaceVariant": "#2A2E39",
    "textPrimary": "#F5F7FB",
    "textSecondary": "#B8C0CE",
    "border": "#747D8E",
    "selected": "#35315C",
    "success": "#56D19B",
    "warning": "#F4B860",
    "errorDanger": "#FF7B72",
    "disabled": "#8D96A6",
    "focus": "#B8B1FF"
  }
}
```

## Accessibility rules

- Normal text and status text must reach at least `4.5:1`.
- Large text, key icons, focus rings, and meaningful control boundaries must
  reach at least `3:1`.
- Selected state uses `selected` plus an active indicator, key icon, font
  weight, or equivalent non-color affordance. Normal selected text and icons
  use `textPrimary`. `brandPrimary` is limited to the active indicator or a
  key icon because light `brandPrimary` on `selected` is only `4.35:1`.
- Success, warning, and error/danger use an icon or explicit text label.
- Destructive actions require a textual verb and confirmation; red is not the
  only signal.
- Platform-generated hover, pressed, ripple, and disabled treatments may
  derive from these tokens, but must not reduce essential text below the
  required contrast.

Measured representative text/status pairs:

| Pair | Light | Dark |
|---|---:|---:|
| `textPrimary` on `surface` | `15.69:1` | `14.63:1` |
| `textSecondary` on `surface` | `6.62:1` | `8.57:1` |
| branded text on `surface` | `5.36:1` (`brandPrimary`) | `8.05:1` |
| `success` on `surface` | `5.37:1` | `8.21:1` |
| `warning` on `surface` | `6.56:1` | `8.87:1` |
| `errorDanger` on `surface` | `6.57:1` | `6.22:1` |
| `disabled` on `surface` | `4.77:1` | `5.27:1` |
| `focus` on `background` | `6.46:1` | `9.19:1` |
| `textPrimary` on `selected` | `12.74:1` | `11.22:1` |
| `brandPrimary` on `selected` | `4.35:1` | `6.17:1` |

Before implementation, automated tests must recompute these values from the
machine table rather than trusting this prose table.

## Platform mapping

Host maps tokens to `Ligase*Color` and `Ligase*Brush` ThemeResources.
Component resources such as NavigationView backgrounds and accent fills must
reference those semantic resources instead of restating hex values.

Android disables Material dynamic color by default. `SYSTEM` follows only the
light/dark mode. Every `ColorScheme` slot used by Ligase must be supplied
explicitly; `lightColorScheme`/`darkColorScheme` defaults must never introduce
an unreviewed Material palette color.

Deterministic Material 3 mapping:

| Material 3 slot | Ligase token |
|---|---|
| `primary` | `brandPrimary` |
| `onPrimary` | `surface` |
| `primaryContainer` | `selected` |
| `onPrimaryContainer` | `textPrimary` |
| `inversePrimary` | `brandPrimary` |
| `primaryFixed` | `brandPrimary` |
| `primaryFixedDim` | `brandPrimary` |
| `onPrimaryFixed` | `surface` |
| `onPrimaryFixedVariant` | `surface` |
| `secondary` | `brandSecondary` |
| `onSecondary` | `surface` |
| `secondaryContainer` | `surfaceVariant` |
| `onSecondaryContainer` | `textPrimary` |
| `secondaryFixed` | `brandSecondary` |
| `secondaryFixedDim` | `brandSecondary` |
| `onSecondaryFixed` | `surface` |
| `onSecondaryFixedVariant` | `surface` |
| `tertiary` | `brandSecondary` |
| `onTertiary` | `surface` |
| `tertiaryContainer` | `surfaceVariant` |
| `onTertiaryContainer` | `textPrimary` |
| `tertiaryFixed` | `brandSecondary` |
| `tertiaryFixedDim` | `brandSecondary` |
| `onTertiaryFixed` | `surface` |
| `onTertiaryFixedVariant` | `surface` |
| `background` | `background` |
| `onBackground` | `textPrimary` |
| `surface` | `surface` |
| `onSurface` | `textPrimary` |
| `surfaceVariant` | `surfaceVariant` |
| `onSurfaceVariant` | `textSecondary` |
| `surfaceTint` | `brandPrimary` |
| `inverseSurface` | `textPrimary` |
| `inverseOnSurface` | `surface` |
| `error` | `errorDanger` |
| `onError` | `surface` |
| `errorContainer` | `surfaceVariant` |
| `onErrorContainer` | `errorDanger` |
| `outline` | `border` |
| `outlineVariant` | `border` |
| `scrim` | light: `textPrimary`; dark: `background` |
| `surfaceBright` | `surface` |
| `surfaceDim` | `background` |
| `surfaceContainerLowest` | `surface` |
| `surfaceContainerLow` | `surface` |
| `surfaceContainer` | `surfaceVariant` |
| `surfaceContainerHigh` | `surfaceVariant` |
| `surfaceContainerHighest` | `surfaceVariant` |

If the installed Material 3 version adds another `ColorScheme` slot, Android
must map it explicitly to an existing reviewed token or return the contract to
review. It must not inherit the factory default.

In Material 3 Android 1.4.0, `scrim` is a `ColorScheme` slot and uses the
mode-specific mapping above. `shadow` is not a `ColorScheme` slot in that
version; Ligase treats it as a custom alpha-bearing overlay primitive with the
same mode-specific mapping: light uses `textPrimary` (`#20232C`) and dark uses
`background` (`#15171D`). Neither is a body-text contrast pair, and neither may
fall back to a Material factory default.

Solid `primary`, `secondary`, and `error` containers use their specified
`on*` token above. Selected application containers use `selected` with
`textPrimary`; they do not infer `onPrimaryContainer` from Material defaults.
Status components use `success`, `warning`, or `errorDanger` with explicit
text/icon semantics.

The `disabled` token is an essential-content readability floor, not merely a
suggestion for deriving opacity. Platform state layers may dim non-essential
decoration, but must not apply an additional whole-component alpha that lowers
essential disabled text below `4.5:1`. Native focus behavior may derive shape
and animation, while the visible focus color remains `focus`.

## Implementation boundary

After cross-platform acceptance:

1. centralize WinUI colors and brushes in `Themes/Colors.xaml`;
2. remove duplicate page/window resource dictionaries;
3. replace safe Ligase-page hard-coded colors;
4. retain documented media viewport colors until a media-overlay contract;
5. build and run Host in light and dark mode on the secondary display;
6. capture navigation, selected state, cards, online/paired, warning,
   error/danger, disabled, and focus evidence.
