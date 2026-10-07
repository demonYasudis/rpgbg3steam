# Generated character sprites

Seven transparent 1536x1024 PNG sheets live in `Assets/Resources/UnitSprites`.
Generated with built-in image_gen; original prompts are in `SPRITE_PROMPTS.md`.

| Gameplay ID | Artwork |
| --- | --- |
| warrior | knight |
| rogue | thief |
| ranger | archer |
| mage | mage |
| ash-crawler | wolf |
| veil-stalker | goblin |
| hollow-brute | orc |
| crypt-warden | knight |
| cinder-keeper | mage |

Enemy mappings are temporary art substitutions, not changes to names, stats, saves or encounter generation.
`UnitSpriteSheet` holds source row boundaries and frame counts; `UnitView` plays idle, walk,
attack, defend (Evade), and hit frames. Facing uses horizontal mirroring because the generated
directional rows are inconsistent. The existing death removal / corpse marker flow is preserved;
death and directional artwork remains in the source sheets but is not played.
The wolf hit row uses only its first four frames to avoid playing dead poses on living units.
Generated poses still need artist cleanup for perfect weapon/anatomy continuity and alignment.
Goblin uses nine frames in every playable row, including idle. Its column edges are measured
at source x = 0, 180, 360, 540, 720, 900, 1070, 1240, 1400, 1536, rather than equal-width slices.
The idle regression check verifies the ninth frame and wraparound. Chest artwork and its
one-shot opening animation are documented in `CHEST_SPRITES.md`.

Texture import uses point filtering, original dimensions, no mipmaps, no compression and source alpha.
Unknown definitions keep procedural placeholder art. Runtime sprites are released with each unit;
shared resource textures are not destroyed by individual views.

The default battle camera now uses 80 screen pixels per world unit, so standing silhouettes
are approximately 64 pixels tall (width follows character proportions). Original source
resolution is preserved. Mouse wheel zooms, right-button drag pans, and the bottom-left button
switches between the full map and character detail. New visible selections center the camera
in detail mode. Window resizing preserves the detail scale. `CharacterCameraChecks.Run`
checks three resolutions, picking, pan, zoom and full-map framing, and writes
`Logs/characters-64-screen.png`.

Validation: `Tools > Guild Tactics > Validate Generated Unit Sprites` or batch
`-executeMethod GuildTactics.Editor.UnitSpriteChecks.Run`. Includes a rendered gallery in
`Logs/wp18-art.png` and movement/combat/ability/visibility regression checks.
