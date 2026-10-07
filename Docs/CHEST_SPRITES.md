# Chest artwork

Generated using built-in image_gen. Source: `Assets/Resources/PropSprites/chest.png` (2172 x 724), four horizontal frames: closed, opening, half-open, open. Runtime creates equal 543 x 724 rectangles. `ChestView` plays once at 6 fps on `ChestOpened`, holds the last frame, follows visibility, and releases runtime sprites on destruction.

## Validation (2026-10-07)

Unity 6000.2.8f1 compiled the changes in an isolated copy of the project. `ChestSpriteChecks.Run`
checks goblin slicing, source-resolution chest import, initial fog visibility, existing expedition
rules, and renders `Logs/goblin-chest-gallery.png`. `ExpeditionChecks.RunBatch` passed the full
existing model/Play Mode suite, including the added check that chest opening holds its final
frame after 0.8 seconds. Logs are saved as `Logs/goblin-chest-check.log` and
`Logs/goblin-chest-play-check.log`. No manual gameplay acceptance was performed.

## Exact generation prompt

Use case: stylized-concept. Asset: dark fantasy pixel-art game chest animation sprite sheet. Exactly FOUR frames in a single horizontal row, equal square cells, total canvas 1536x384 or similar 4:1. Each cell shows the SAME small medieval wooden treasure chest with black iron bands, bronze latch, muted brown wood, crisp detailed pixel clusters, dark outline, overhead three-quarter RPG view facing southeast, upper-left light. Frame 1 closed chest, frame 2 lid slightly raised, frame 3 lid halfway open showing restrained warm gold glow, frame 4 lid fully open with a few gold coins inside. Fixed camera, identical scale, identical stationary base position and ground baseline across all four frames; only lid rotates on its rear hinge. Each entire chest and lid fits comfortably in its own cell with large margins. Actual transparent background, no floor, no shadows beyond object, no text, no separators, no grid, no inset artwork. These are four sequential game animation frames, not a scene.

