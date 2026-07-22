# Release runbook

Releases are cut with `release.ps1` at the repo root. It is the only sanctioned way to
release: it bumps every version location, validates, builds, packages the Thunderstore
zip, commits, tags, pushes, cuts the GitHub Release, and publishes to Thunderstore.

## One-time setup

- **.NET SDK** and **GitHub CLI** (`gh auth login`) installed.
- **Thunderstore CLI**: `dotnet tool install --global tcli` (ensure `%USERPROFILE%\.dotnet\tools`
  is on PATH). The publish target lives in this repo's committed `thunderstore.toml` (community
  `dyson-sphere-program`); it holds no secret.
- Set the Thunderstore API token as an environment variable, kept out of every repo:
  `setx THUNDERSTORE_API_TOKEN "<your-token>"` (new shells only). This is the only remaining
  prerequisite before an automated publish will succeed.

## Before you run it

Version lives in three files and the script keeps them in lockstep from the `-Version`
argument, but the human-authored content is on you first:

1. Land and commit all feature/fix work. Confirm it runs in-game (a plain `dotnet build`
   deploys into the r2modman profile via the csproj's post-build step).
2. Add a new `CHANGELOG.md` section at the top for the version you are about to ship,
   describing the user-visible changes.
3. Update `README.md` if behaviour, config, or keybinds changed.
4. Confirm `manifest.json` `description` still reads true and `dependencies` are current.
5. Confirm `icon.png` is present and 256x256.
6. Pick the semver bump (patch / minor / major) - that number is your `-Version`.

The script re-checks all of the above and refuses to proceed if anything is missing,
including a scan that blocks any tool/authorship attribution from reaching a pushed file
or commit message.

## Running it

```powershell
# standalone mod
./release.ps1 -Version 0.3.4

# a mod that depends on CruiseAssistPlus: pin the CAP version you build against
./release.ps1 -Version 0.3.2 -DepVersion 0.3.4

# validate + build + package only, no git/release/publish (leaves versions bumped locally)
./release.ps1 -Version 0.3.4 -DryRun

# everything except the Thunderstore upload
./release.ps1 -Version 0.3.4 -NoPublish
```

## Releasing more than one mod together

Dependencies build in order. Release the base first, then anything that pins it:

1. `CruiseAssistPlus` - `./release.ps1 -Version <cap>` (rebuilds its DLL).
2. `AutoPilotPlus` - `./release.ps1 -Version <app> -DepVersion <cap>` (re-pins and rebuilds
   against the fresh CAP DLL).
3. `AutoQueueBuild` - independent, release any time.

## Notes

- `dist/` is gitignored. The packaged zip is attached to the GitHub Release and uploaded to
  Thunderstore; it is never committed.
- A dry run leaves the bumped version numbers in the working tree. Revert with
  `git checkout -- .` if you decide not to ship.
