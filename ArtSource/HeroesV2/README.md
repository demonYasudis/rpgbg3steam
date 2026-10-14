# Hero art V2 — 2026-10-14

The owner approved the illustrated guild/battle direction and requested small
animated gameplay sprites below the class portraits, plus matching hero sprites.
Generated with the built-in image_gen tool using `guild-reference.png` as the
character/style reference. Original characters: exposed-face bearded warrior in
silver plate/red cape, hooded female rogue with daggers, auburn-haired female
ranger in green, elderly silver-haired mage in blue. Source prompts: PROMPTS.md.

## Outputs and integration

- `warrior.png`, `rogue.png`, `ranger.png`, `mage.png`: original generated sheets.
- `packing-report.json`: measured source rectangles, body anchors and scale.
- `../../Assets/Resources/UnitSprites/hero-*-v2.png`: runtime atlases, each
  1024×1280, four columns and five rows of 256×256 cells.
- Row order: Idle, Walk, Attack, Defend, Hit. Four distinct frames per row.
- The common foot baseline is y=224 from the top of each cell. Nearly transparent
  gutter noise (alpha <=16) is removed; packing fails if clean row/column gutters
  cannot be found. Artwork is cropped/packed and nearest-neighbor scaled, not redrawn.
- UnitSpriteSheet maps only the four hero IDs to these atlases. Enemy mappings and
  the old knight/thief/archer/mage textures are retained.
- Frame rates: idle 4, walk 8, attack/defend 5, hit 10 fps. Attacks retain the
  existing 0.8-second presentation window; hits retain the 0.4-second window.
- Per-hero pixels-per-unit values normalize measured idle bodies to approximately
  0.82 world units. The foot baseline sits 0.3 world units below the unit origin.

Repack with Node.js and sharp available in the art environment:

```text
node Tools/Art/PackHeroSprites.cjs ArtSource/HeroesV2 Assets/Resources/UnitSprites
```

Unity's existing UnitSpriteImporter supplies Point filtering, no mipmaps,
uncompressed textures and transparency. Keep generated `.meta` files.

## Validation and limits

UnitSpriteChecks passed in Unity 6000.2.8f1: atlas loading, frame rectangles,
filtering, hero/enemy separation, animation timing and existing model checks for
movement/combat/abilities/visibility. Full model and Play Mode runner also passed
(Logs/hero-v2-check.log and Logs/hero-v2-play.log).

The updated illustrated guild remains an animated browser mockup, not a replacement
for GuildUI in Unity. It displays these same packed sprites below the portraits
and in a sample initiative strip; idle loops, click-to-attack, ambient light,
pause and reduced-motion support are included. Existing UI task documents are
not re-planned in this step, as the owner asked to inspect the mockup first.

Generated sprites have four poses per animation and one facing direction; the
existing horizontal flip remains. They are not eight-direction atlases or new
death animations. Portrait breathing in the mockup is subtle scale motion, not
an independently painted portrait animation. Windows player build and manual
mouse-driven acceptance of the Unity scene were not performed in this step.
