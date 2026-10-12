# Character scale (phase 2) — design notes

Branch: `claude-port`. Base/HEAD: `d0d3ff2`. No commit or global configuration changes.

## Changed files

- `prototype/CompanionStore.cs`: persisted CharacterScale, default/coercion 1 or 2.
- `prototype/CompanionPanel.cs`: independent appearance switch and separate hints.
- `prototype/CompanionFeatures.cs`: save/rollback/live refresh, character-relative approach distances and delivery clearance.
- `prototype/PetWindow.cs`: contact-centred sprite geometry, live scale, stable box, speech placement, carry offsets, speeds and offscreen fixture support.
- `prototype/PetCompanionVisuals.cs`: box-derived enlarged todo offsets; enlarged speech/todo avoidance.
- `prototype/Interaction.cs`: wander clearance and cursor approach offset.
- `prototype/DesktopSeats.cs`: scaled character space requirements.
- `prototype/WindowPerch.cs`: scaled edge/headroom requirements and canonical calibration input.
- `prototype/PoseCalibration.cs`: documented canonical units, inverse-scaled input and live carry calibration placement.
- `prototype/PutdownAdjustmentWindow.cs`: identifies displayed offsets as 1x canonical units.
- `prototype/PointerVerification.cs`: runs pointer checks at both scales and invokes the new offscreen suite.
- `prototype/DragSurfaceVerification.cs`: runs both scales.
- `prototype/CharacterScaleVerification.cs`: new offscreen geometry, persistence, calibration, UI and PNG verification.
- This report.

## Geometry and units

CharacterScale multiplies the 128-DIP base sprite canvas, per-clip DisplayScale result, pivot displacements, SurfaceStandingPosition deltas, PoseOffset / SurfaceOffset / CarryOffset, hand attachment location, stable CharacterBox, character-relative cursor approach offsets and required head/side/surface clearance. Seat, Sleep, Pickup, Putdown, WindowSeat and WindowSleep corrections all scale, including independently saved right-facing pickup/putdown values.

PoseCorrection values are **1x local display units**, not 256-pixel source-art units. Right facing mirrors X, including independent right-facing records. Rendering multiplies by CharacterScale. Calibration drag/arrow deltas divide by CharacterScale before saving: 10 display DIPs at 2x stores 5 canonical units. Existing JSON corrections require no migration. The original ±60 canonical limit remains.

For base size B, surface pivot p, canonical correction c, anchor A and scale s, sprite origin is `A - p*(B*s) + c*s`. The calibrated source-art contact is `p*B - c`, so the rendered contact stays at A. Pickup/putdown standing offsets similarly preserve the calibrated icon-target contact. The attachment follows the animated hand coordinate and retains its own source dimensions; the hand can move within an animation. Original 1x origin/status formulas are independently reproduced in verification.

Desktop positions, icon dimensions (including the carried copy), window rectangles/edges, target markers, icon destination/grid coordinates, screen work areas and UI/work-area padding do not scale with CharacterScale. UI sizes still follow UiScale alone. Character clearances are separate from fixed desktop/UI margins. Drag thresholds remain input distances; lifted sprite geometry and drop feet use the enlarged size.

Nominal walking/pulling speed: 110 → 220; carrying: 90 → 180; running: 210 → 420 (desktop movement units/s). Brake distance cap: 32 → 64. Existing whole-cycle quantization, animation playback rates and easing remain; these are nominal speeds, not a claim of constant instantaneous velocity. Changing scale during a trip replans the remaining trip at the new size. The fixed-scale 1x timing path is unchanged.

HighQuality bitmap resampling is retained. The base canvas becomes 256 at 2x, but existing fractional per-clip DisplayScale and monitor DPI mean every clip is not necessarily an exact 1:1 texel mapping.

## Final verification

- `npm run build`: PASS, 0 warnings, 0 errors.
- `--verify-pointer`: PASS, exit 0; 6 pointer groups at 1x and 6 at 2x.
- Existing UiScaleVerification: PASS, 15 evidence groups.
- CharacterScaleVerification: PASS; 23 clips, all 2,634 frame/direction cases, 21,269 assertions, 14 offscreen PNGs. Contact tolerance is stricter than 0.5 DIP. Includes independent legacy formulas, scaled hands/unchanged attachment dimensions, calibrated pickup/putdown targets, window contacts, disk coercion, actual appearance switch, actual fake-seat calibration saves, and space requirements.
- `--verify-drag-surface`: PASS, exit 0; 24 grab/move/drop cycles at each size (48 total), 3 evidence groups per size.
- `--verify-social-music`: PASS, exit 0; 16 evidence groups.
- `--verify-animations`: PASS, exit 0; 13 evidence groups.
- Normal pet + `npm test`: PASS, 30 passed / 0 failed / 0 skipped. First attempt started before the pipe was ready and had 27 passed / 3 connection failures; after a successful status readiness check, the complete suite passed.
- `git diff --check`: PASS.
- Normal 1x idle status: renderSize 135.59172413793104, characterHeight 102.4; realMoveAllowed and cursorAllowed false.

No forbidden verification mode was run; no real icon/mouse/window movement permissions were enabled. New PNGs use an unshown WPF visual and fake 48×48 icon/window line, not desktop screenshots.

## PNGs and machine-readable evidence

All are beside the exe in `C:\coding\WhaleAlive\prototype\bin\Release\net9.0-windows\`:

- `char-scale-1x-idle.png`, `char-scale-2x-idle.png`
- `char-scale-1x-icon-seat.png`, `char-scale-2x-icon-seat.png`
- `char-scale-1x-window-seat.png`, `char-scale-2x-window-seat.png`
- `char-scale-1x-window-sleep.png`, `char-scale-2x-window-sleep.png`
- `char-scale-1x-carry.png`, `char-scale-2x-carry.png`
- `char-scale-ui-1x-character-1x.png`
- `char-scale-ui-1x-character-2x.png`
- `char-scale-ui-2x-character-1x.png`
- `char-scale-ui-2x-character-2x.png`

JSON evidence: `char-scale-verification.json`, `pointer-verification.json`, `pointer-2x-verification.json`, `ui-scale-verification.json`, `drag-surface-verification.json`, `drag-surface-2x-verification.json`, `social-music-verification.json`, `animation-verification.json`.

## Compatibility and human acceptance

Speech placement (follow-up fix): the speech bubble avoids the character box and every todo bubble individually (UiSizing.Beside overload), in all four UI/character combinations, preferring a spot above the head shifted away from the todo arc. This also removes a small overlap with the top todo that existed in the original 1x layout. CharacterScaleVerification checks speech/todo separation for every combination. Note: the char-scale-1x-idle.png render appears offset from the anchor marker because the VisualBrush capture aligns to content bounds; the canvas-coordinate assertion ("1x idle feet on anchor") confirms the real placement.

Real-icon seating, real-window sitting/sleeping, mixed-monitor DPI appearance and perceived gait contact still need human visual acceptance. Offscreen math/drag tests do not establish that every source-art anatomical contact or previous user calibration looks ideal on every real desktop.

Original companion.json is restored byte-for-byte; both scale properties were absent, so both remain effective 1. SHA-256: `AD67F978AD82F9C41D470E32E296279A39757D67E0ABFD0F456DA6A1512A0C84`. Final launch uses the user's existing Startup/Whale Alive.lnk (which targets the built Release exe).
