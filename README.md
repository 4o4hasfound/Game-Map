# The Tape

A first-person horror game in three parts, about 15 minutes, for teachers and students. Unity 6.3 LTS (6000.3.26f1), URP 17.3.

You put an unlabelled tape in your VCR and get pulled into it: the Grand Meridian Hotel on its last night, 31 October 2003. Something walks the hotel. It has no face and poor eyes: it sees you only in bright light. **It hunts by sound.**

## Play

Run `Build/TheTape.exe`. Headphones are recommended (binaural sound).

1. **Brightness.** Adjust until the darkest square is barely visible.
2. **Camera choice.** Use the camera (head tracking), show your face in the game, or play without it. Pictures are processed on this computer only and never saved or sent. The game makes no network connections.
3. **Calibration (camera only), about 10 s.** Sit normally, then lean your head left and right.
4. **Your house (a few minutes, or as long as you like).** A warm, cozy place, real life (a clean picture: the tape's look comes later). You wake up in bed: hold E and keep the pointer steady with ← → to get up (the same pointer you'll use to ease doors). Nearly everything can be used: eat the fruit, open the fridge, make toast, put the kettle on, switch the radio and the lamps, warm your hands at the fire. The doorbell rings, then someone keeps knocking until you come to the front door (it's locked: you're staying in); then an envelope drops through the letterbox: the tape and a card, "PLAY ME". Things move the way they would when you use them:
   - you pick up a photo, a clock or your keys and turn it to look at it;
   - a book slides out of the shelf to be read and goes back;
   - food comes to your mouth and gets smaller bite by bite; drinks tip; the teapot pours;
   - the kettle steams, the tap runs, the toast pops up;
   - the crooked painting slips back after you straighten it;
   - light switches flip and stay flipped, and the letterbox flap snaps shut;
   - you lean in to the fire or into the basket, and sit down on the sofa.

   Put the tape in the VCR under the TV in the living room, steady the rolling picture (← →), and watch. You kneel in front of the TV, so the picture fills your sight. In colour, it shows the hotel lobby, filmed on a camcorder someone left on a tripod with its lamp on. A figure walks straight up to the lens while your lamps stutter. When its pale, faceless head fills the picture, the lamps die and the TV pulls you in. The tape only shows what's happening; the rules are in the letter waiting for you at the hotel.

**How it works**

- **Everything is a noise.** Footsteps depend on the floor: carpet is quiet, lino a little louder; marble, broken glass, rubble and water are loud. Running, doors that swing or slam, and panting after a run all carry. The listener hears through the same sound propagation you do: closed doors muffle, open doorways carry.
- **It follows your sounds.** Crouched (stalking) you make no footstep noise; walking it hears you a few metres away (on marble about 15 m, on carpet about 3 m); running carries far, and close by it makes it rush. Each step of yours it hears moves it to where you are now, and when it hears you clearly it hurries (2.0 m/s: faster than you walk, slower than you run). You hear it react: a sharp breath.
- **Its eyes are poor.** It sees you only in obviously strong light: right under a working lamp, next to a desk lamp, or with your camera light on (pointed at it, from about 22 m). In the dark and in ordinary dim light it can't see you, and crouched you stay hidden in brighter light than standing. The **visibility** meter (bottom left) shows how you stand: hidden, dim, visible, or visible because of your own light.
- **It goes to the loudest thing it hears.** Phones (the front office switchboard rings the guest rooms), radios, TVs, the jukebox, pianos, the chapel organ, music boxes, washing machines, the generator, thrown bottles. It stands over the noise listening, then smashes it.
- **It's slow until it hears you close.** In part one it wanders at 1.0 m/s and investigates at 1.45 m/s (you walk at 1.6). It rushes at 2.7 m/s only when it hears you close by. Then run (3.8 m/s for 8 s) and go quiet. It gets sharper and quicker each part: in part three it investigates at 1.75 m/s and rushes at 3.0 m/s.
- **Lures hold it for a while.** At a lure it stands listening, then smashes it: a radio holds it a few seconds; the generator 14 s; the roof PA horn 16 s.
- **Hide.** Use wardrobes, beds, baths, lockers, carts, cages, pews, tables or curtains. Keep still and hold your breath when it's close. With the camera on, moving your head while hidden makes a noise.
- **Bolts.** Guest-room doors, the recorder room, the pump room and the antenna room have a bolt on the inside. It can't open a bolted door: it beats on it and gives up. During the lift call and the master tape it breaks a bolt after four blows, so a bolt alone won't save you.
- **Between floors.** Now and then it goes into the service lift shaft (you hear it climbing) and comes out on your floor of the part you are in. That is your time to explore.
- **The camera light.** The camcorder has a video light (F), on when you arrive. It is the one thing it does see: switching it on in front of it is a mistake. The switch clicks, and it hears that from a few metres. The battery is small: about 30 s of light, then switch it off and it charges back fully in about 12 s. Use it in short bursts. It stutters when nearly flat.
- **Doors.** A tap of E slams a door open or shut: fast and loud, for getting away. Holding E eases it: a pointer drifts along a bar under the prompt and you keep it in the middle with the ← → arrow keys. While it stays in, the door eases quietly (shut to open in about 2 s); if it slips out, the door swings the rest of the way, and the noise is as big as what was left (a slip at the start is heard through the floor, near the end only close by). Let go of E to stop where you are.
- **Stamina** (the bar at the bottom centre) goes on running and on crouching. Out of it you straighten up and your steps are heard again.
- **Finding your way.** Doors seen from a corridor have brass plates: room numbers on the guest rooms (201-219, 901, 903), names on the rest (MANAGER, ELECTRICAL, PUMP ROOM, ANTENNA). Direction signs hang where you arrive on a floor. A fire evacuation plan ("YOU ARE HERE") hangs at the start of each part and at the stair heads: film it (E) and that floor's plan goes on the camcorder map (M). Without it the map shows only the rooms you have walked through. The map marks where this part's fire plans hang (a diamond), and names a floor that has one.
- **Caught.** The tape skips back to the last checkpoint of the part you are in, and your face on the tape wears away. Three times and you are erased.

**The three parts.** Each is a separate stage with its own goal and its own way on. Only the part you are in is loaded and drawn.

| Part | Where | Goal | The way on |
| --- | --- | --- | --- |
| 1. The Hotel | ground floor and guest floor | Read the letter. Find both halves of the safe code: behind the dresser in Room 214 (east wing), and in the bag in Room 209 (west wing, through the hole in burnt 207). Open the manager's safe (ground floor) for the basement key. | Unlock the basement door and go down the stairs (they collapse behind you). |
| 2. The Basement | B1 | The breaker has no fuse: get the spare from the storage cages, then throw the breaker (loud). Get the lift key from the recorder room. | Call the service lift from the pump room: 30 s of bell, and it comes for it. Start the generator first, or bolt both doors, or hide and come back. Then ride it up. |
| 3. The Roof | penthouse floor and roof | The roof door needs the owner's keycard. His suite is chained, so go in through Suite 901 or 903 to the study. | Up to the stormy roof. Switch on the PA horn in the far corner, bolt yourself into the antenna room and hold eject for 18 s. Let go and the tape winds back; without a lure it breaks the bolt and gets you. |

**Controls**

| Key | Action |
| --- | --- |
| WASD | Move |
| Shift | Run (uses stamina: about 8 s) |
| C | Crouch: no footstep noise, harder to see; uses stamina (about 16 s moving) |
| Space (hold) | Hold your breath (10 s, then you gasp) |
| E / left click | Use. On a door: tap to slam it open or shut (loud); hold to ease it, keeping the pointer in the middle with ← →; let go to leave it part open |
| ← → | While easing a door: keep the pointer in the middle |
| Mouse wheel | Nudge the door you're looking at |
| F | Camera light on/off (about 30 s of light; switch it off and it charges back in about 12 s) |
| M | The camcorder map of the floor you are on |
| Right click / G | Throw a bottle, or set the portable radio down |
| Q / R | Lean (with the camera, lean your head) |
| Esc | Pause and settings |
| F3 | Change the audio output mode |
| F9 / F10 | Testing: skip the current objective / the rest of this part |

**Settings:** low scare, classroom mode, picture (clean / tape / worn), brightness, mouse sensitivity, sound captions, the VCR display, head lean, audio output.

**The interface** keeps out of the way: a light sans-serif and thin grey bars that fade out at both ends, each shown only while it matters (stamina and breath at the bottom centre; the light's battery and your visibility at the bottom left; a hold's progress and the door pointer under the prompt). The VCR display (PLAY, the date) keeps the camcorder's own type.

## The look

- **Camcorder:** a handheld 90s camcorder on VHS. The camera has spring-lagged look, step bob (kept small when you run), breathing sway and shake. The picture has auto exposure that gains up in the dark (with clumpy noise), a faint CCD smear from bare bulbs, motion-blur ghosting in low light, interlace combing on fast pans, autofocus hunting, auto white balance that drifts, colour bleed, tracking errors and head-switching noise.
- **Surfaces:** photo-scanned CC0 materials from ambientCG (see `ASSET_CREDITS.md`) with normal, roughness and occlusion maps, a grime layer against tiling, and screen-space ambient occlusion.
- **Decay:**
  - grime at the foot and top of every wall, water stains running down from leaks, mould, soot over the burnt rooms;
  - wallpaper peeling in strips, missing and hanging ceiling tiles, live cables that arc;
  - paper and fallen tiles on the floor, boarded windows.
- **Light:**
  - **Fittings:** real ones (tubes in housings, flush domes, fabric shades, bare bulbs on cords, a brass chandelier with dead bulbs, twin-head emergency units, battery EXIT signs, a neon sign in the bar). Colours are what a camcorder on auto white balance records: incandescent a warm white, fluorescent near neutral, emergency heads white. Each fitting varies a little in colour and strength. EXIT signs glow but hardly light the wall. 9 of the 94 tubes, sconces and bare bulbs are dead.
  - **Camera light:** a warm, wide video light on the camcorder (F). Close to a wall it dims itself, like an auto iris, so it doesn't burn a white disc into the picture.
  - **Flickering:** flickering ceiling lights cast moving shadows.
  - **Shadows:** the three shadow-casting lamps nearest you cast soft-edged shadows at any moment; the rest light without them, which keeps the GPU cost low. Your camera light's shadows are soft too.
  - **Power:** a floor browns out or blacks out every half minute or so (the battery lights stay on).
  - **Storm:** lightning flashes between the boards of the windows, followed by real recorded thunder (CC0).

## The map

The `Map/` folder has the map:
- `map.html`: interactive, with every room's purpose, door, lure, hiding place, hazard, note, camera and lamp, and the listener's routes. Open it in a browser and click anything.
- `map_<floor>.png`: a picture of each floor.
- `map_export.json`: the data behind them.

Five floors in three parts, plus your bedroom: part one is the ground and guest floors of a 60 × 44 m building, part two its basement, part three the penthouse and roof at the top of the tower. Every room has a job: a lure, a tool, a hiding place, a quiet or loud route, a camera or a clue. The interactive map is `design/map_final.html` (open it in a browser). The room list and every room's purpose are in `Assets/Scripts/Core/LevelLayout.cs`.

- **Guest floor (2F):** galleries round the lobby atrium, the ballroom (where it waits), two guest wings, Room 214 (the key).
- **Ground floor:** the front office (start), the lobby (marble, rugs as quiet paths), the restaurant, chapel, bar, kitchen, the security office (CCTV of every floor), and staff passages behind everything.
- **Basement (B1), part two:** the electrical room (breaker), the storage cages (the fuse), the recorder room (the lift key), the pump room (the lift), the boiler (covers noise while it roars), the generator (the best lure), laundry, wine cellar, a flooded passage, the archive.
- **Penthouse, part three:** it looks like nowhere else in the hotel. The owner's private floor is dark walnut, black marble and parquet, lit by table lamps, picture lights and the cold glow of monitors. Portraits of earlier viewers, their faces worn away, line the corridor. The chained owner's suite is his screening room: a wall of CRTs, shelves of tapes, cables, a bar, a fireplace, a grand piano. The study has the keycard, his diary and a board of faces; the bedroom has a four-poster. The ways in are a one-metre servants' passage behind the walls, or Suite 903. Also the butler's pantry, the plant room (air handler lure) and the roof stair.
- **Roof, part three:** gravel and puddles in the storm, water tanks and air units to hide behind, the PA horn on its mast, sheet-metal huts: the lift motor room (its lair) and the antenna room, crammed with racks and a wall of snow-filled screens, where the master tape is.
- **Narrow and broad:** every part has both. Part one: the lobby atrium, ballroom and restaurant, and the 2 m staff passages. Part two: the storage cages and boiler room, and the corridors and wine cellar aisles. Part three: the owner's suite and the open roof, and the servants' passage.

## Build

```
Unity.exe -batchmode -quit -projectPath TheTape -executeMethod TapeBuilder.BuildAll -logFile build.log
Unity.exe -batchmode -quit -projectPath TheTape -executeMethod MapExport.Run -logFile export.log
python design/map_viewer.py "The Tape: final map" design/map_final_fragment.html design/map_final.html
```

- **What the builder makes:** `Assets/Editor/TapeBuilder*.cs` generates the materials and textures and builds the scene `Assets/Scenes/Main.unity` from `Assets/Scripts/Core/LevelLayout.cs` (rooms, doors, stairs, decay). It then builds `Build/TheTape.exe`. Fix the builder or the layout, not the generated scene.
- **Map export:** `MapExport.Run` writes `design/map_export.json` from the built scene; `design/map_viewer.py` turns it into the interactive map.
- **Player only:** `TapeBuilder.BuildPlayerOnly` rebuilds the executable without regenerating the scene.
- **Placement audit:** every scene build checks where things were put and writes `../verification/thetape/v4/placement_audit.txt`. It looks for:
  - anything poking into a doorway or standing in front of a door;
  - small things floating above, or sunk into, what holds them;
  - furniture overlapping other furniture or pushed through a wall.

  The map has 0 issues.
- **Map and design tools:** `design/` and `../verification/` are outside this folder, in the full workspace; the exported map is in `Map/`.

## Automated checks

```
Build/TheTape.exe -autotest -autotestOut ../verification/thetape/v3/autotest.txt
Build/TheTape.exe -capture ../verification/thetape/v3/shots
Build/TheTape.exe -autotest -finaleRepeat 20 -autotestOut ../verification/thetape/v3/finale_repeat.txt
Build/TheTape.exe -playthrough -playthroughOut ../verification/thetape/v4/playthrough.txt
Build/TheTape.exe -playthrough -playthroughFrom 3 -playthroughOut ../verification/thetape/v4/playthrough_p3.txt
```

`-playthrough` plays the whole game (the bedroom, all three parts, the end screen) with real walking physics: it follows the navmesh, opens doors in its way, and uses each thing only from a spot where a player could see it and reach it. It reports stuck spots, things a player couldn't use, frame hitches (and where), errors, and the time per part, and ends with `RESULT SMOOTH` or `PROBLEMS`. The listener is kept out of its way; the autotest checks the hold-outs with it. `-playthroughFrom 2|3` starts at part two or three.

`-finaleRepeat N` runs only the finale check N times on fresh scenes and logs the listener's notes each time, for chasing intermittent failures.

- **What they cover:** the camera light (on at arrival, battery drain, a spare refills it, dimming against a wall), the final picture (not black, not blown white), the storm (lightning lights switch on and off), a forced blackout (tubes off and back, emergency lights on), arcing cables, the decay counts, the prologue, the letter and objectives, hearing levels by floor and through doors, floors and stairwells, its speeds against yours, investigate and rush, the calm start, masking, a radio lure, a phone lure, bolted doors (and the finale breaking a bolt), door easing and tapping, breath holding and gasping, hiding (still vs rustling), being caught, reachability on all three floors, everything you can use aimable from walkable floor in every stage, the knocking heard from the bed, hiding places and lures per part, a performance route, the face model self-test, a bot run through the whole chain to the "OUT" ending, the finale with the listener coming (a spark behind it on the way must not turn it back), and three catches.
- **What they don't cover:** they are scripted checks, not a playtest, and they cannot test a real webcam.

## Credits

- **Audio:** the sound bank and audio system come from the earlier `HotelGame` project; the house's sounds are real recordings. All are listed with their sources and licences in `AUDIO_CREDITS.md` (some are CC BY: their credit lines are there).
- **The camcorder look:** the handheld camera and the VHS image chain are ported from the earlier `HotelGame` (`VHSCamera.cs`, `VHSPostFX.cs`, `VHSFX.shader`).
- **Surface photos:** 23 CC0 materials from ambientCG (https://ambientcg.com), listed in `ASSET_CREDITS.md`.
- **Your house:** CC0 models and textures from Poly Haven (https://polyhaven.com), listed in `ASSET_CREDITS.md`.
- **Face detection:** BlazeFace short-range (Google MediaPipe), converted to ONNX by Unity, from `huggingface.co/unity/inference-engine-blaze-face` (Apache-2.0). It runs locally with Unity Inference Engine.
- **Everything else** (the hotel's furniture and fittings, the figures, the level) is generated by code in this project.
