---
name: github-release
description: Use this skill when the user asks to create a GitHub release, publish a release, cut a release, or make a new release of EmoTracker. Handles version bumping, committing, waiting for CI, and publishing a release with build artifacts.
---

# GitHub Release

Use this skill to publish a new GitHub release of EmoTracker. Follow every step in order — do NOT skip steps or assume defaults.

## Step 1: Interview the user

Before doing anything else, ask the user for the following information (use the AskUserQuestion tool if available, otherwise ask directly):

1. **Branch** — Which branch should the release be built from? (e.g. `avalonia`, `main`)
2. **Version number** — The new version number in `Major.Minor.Build.Revision` form (e.g. `3.0.2.0`).
3. **Prerelease** — Is this a prerelease? (yes/no)

Do not proceed until you have all three answers.

## Step 2: Update assembly versions

Update `AssemblyVersion` and `AssemblyFileVersion` to the user-specified version in ALL of these files:

- `EmoTracker/Properties/AssemblyInfo.cs`
- `EmoTracker.UI/Properties/AssemblyInfo.cs`
- `EmoTracker.Data/Properties/AssemblyInfo.cs`
- `EmoTracker.Core/Properties/AssemblyInfo.cs`

Both attributes in each file should be updated:
```csharp
[assembly: AssemblyVersion("X.Y.Z.W")]
[assembly: AssemblyFileVersion("X.Y.Z.W")]
```

After editing, build the solution to confirm it's clean:
```
dotnet build EmoTracker/EmoTracker.csproj
```
Abort and report the error if the build is not clean (0 errors).

## Step 3: Commit and push

Make sure you're on (or pushing to) the branch the user specified.

Commit the assembly version changes with a message like:
```
Bump assembly versions to X.Y.Z.W
```

Include the standard `Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>` trailer.

Push to the remote branch the user specified:
```
git push origin HEAD:<branch>
```

Note the commit SHA — you'll need it in Step 4.

## Step 4: Wait for CI build to pass

EmoTracker uses GitHub Actions to produce build artifacts. After the push, poll for the workflow run that corresponds to the commit you just pushed, and wait for it to complete successfully.

Use `gh run list` to find the run and `gh run view <run-id>` to check status. Use `gh run watch <run-id>` if available to block until completion.

```bash
# Find the run for the commit
gh run list --branch <branch> --limit 5
# Watch the run until it completes
gh run watch <run-id>
```

If the run fails, report the failure to the user and stop. Do not proceed to create a release on a failed build.

## Step 5: Create the release

Once the build has succeeded:

1. **Determine the tag name**: `X.Y.Z.W` for a stable release, or `X.Y.Z.W-preview` for a prerelease.
2. **Download all artifacts** from the successful workflow run:
   ```bash
   gh run download <run-id> --dir ./release-artifacts
   ```
3. **Gather release notes content**:
   - Use `gh release view --json tagName` (or `gh release list`) to find the last release's tag.
   - Get the commit log between the last release tag and the current commit:
     ```bash
     git log <last-tag>..HEAD --oneline
     ```
   - Summarize the changes into a concise description of what's new in this release.
4. **Create the release** targeting the commit on the specified branch, uploading all downloaded artifacts, and using GitHub's auto-generated notes plus your summary:
   ```bash
   gh release create <tag> \
     --target <branch> \
     --title "<tag>" \
     --generate-notes \
     --notes "<your summary of changes since the last release>" \
     [--prerelease] \
     ./release-artifacts/**/*
   ```
   - Add `--prerelease` ONLY if the user said this is a prerelease.
   - `--generate-notes` gets GitHub's automatic "what's changed" section; combine with `--notes` to prepend your own summary. If both can't be combined in a single invocation, create the release with `--generate-notes` first, then edit it with `gh release edit <tag> --notes "<combined notes>"`.

5. **Verify** the release was created successfully and report the release URL to the user.

## Important notes

- Never skip the interview step — always confirm branch, version, and prerelease status before acting.
- Never create a release from a failed or in-progress build.
- Never force-push or bypass branch protection. If the branch is protected and direct push fails, report to the user and stop.
- Always use a HEREDOC for commit messages to preserve formatting.
- The existing repo has commits like `6f8fc1e Bump assembly versions to 3.0.1.0` — match that commit message style.
