# Game Map — Hotel Meridian and Hotel Bellwether

A first-person VHS horror game containing two separately authored abandoned hotels, each with three playable levels, paired elevators, interconnected routes and geometry-aware spatial audio.

## Open the project

1. Install Git LFS, then clone this repository with Git. If already cloned, run `git lfs pull` to download the binary assets.
2. Add the repository root directory to Unity Hub as a project.
3. Open with **Unity 6000.6.7f1** and allow Unity to restore packages and import assets.
4. Open `Assets/Scenes/Hotel.unity` (Meridian) or `Assets/Scenes/HotelBellwether.unity` (Bellwether), then press Play.

The scenes, FBX maps, recorded audio, materials, textures, scripts, package lock and project settings are included. Library, temporary files, player builds, logs and test recordings are excluded.

## Build

Choose **Hotel > Build Player** to build both existing scenes into `Build/HotelMeridian.exe`.
Choose **Hotel > Build Everything** to regenerate both scenes from the included FBX maps and layout metadata before building. The recorded sound catalog is included; no synthetic sound baking is used.

Batch build from the repository root:

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.6.7f1/Editor/Unity.exe' -batchmode -quit -projectPath . -executeMethod HotelBuilder.BuildAll
```

The Blender generators and recording preparation scripts live in the author's larger workspace. They are not required to open, play or build this Unity project.

## Environments

- **Meridian:** guest wings; a lobby, mezzanine, escalators and ballroom; a basement with machinery, elevated walkways, staff facilities, shelter and flooded cellar routes.
- **Bellwether:** guest terraces and a conservatory; a winter garden, ballroom and service circuits; thermal baths, a pool promenade and filtration infrastructure.

Successive elevator connections occupy different locations, encouraging exploration across each floor.

## Controls

| Input | Action |
| --- | --- |
| WASD / mouse | Move / look |
| Shift | Run |
| Ctrl / C | Crouch |
| E / left click | Use; hold and move the mouse to push or pull a door |
| F | Flashlight after collecting it |
| F6 | Switch hotels |
| Mouse wheel / Q / R | Tune a radio |
| 1–0 | Play piano keys |
| Right mouse / G | Throw a carried object |
| M / N | Release / remove the optional test stalker |
| F1 / F2 / F3 | Audio diagnostics / direction test / output mode |
| H / Escape | Help / pause |

Walking does not move doors. Running can burst them open. Doors stay ajar and their aperture affects sound propagation. Seats, readable papers, carryable props and interactive sound sources are included.

## Rendering and sound

Windows graphics jobs are disabled for compatibility with the tested Intel Iris Xe driver. Spatial render chunks and a bounded reflection scheduler reduce graphics command pressure. Reopen Unity after changing graphics backend settings.

Sound uses recordings and sample performances. Propagation combines material transmission, paths through openings, door aperture, sampled early reflections and geometry-derived late reverberation. It is a real-time approximation. Acoustic simulation covers the active gameplay floor and its split elevations; vent transmission from inactive floors is not implemented.

Read [AUDIO_CREDITS.md](AUDIO_CREDITS.md) for attribution, licenses, source fidelity and editing disclosures. Related mechanisms and instrument tunings may share recordings.

## Runtime checks

The built player supports `-hotel bellwether`, `-navtest <report>`, `-acoustictest <report>`, `-mixreview <directory>`, `-stress <directory>`, `-capture <directory>` and `-swaptest <report>`.

Automated measurements do not replace headphone listening or manual gameplay review. The optional stalker uses test art.
