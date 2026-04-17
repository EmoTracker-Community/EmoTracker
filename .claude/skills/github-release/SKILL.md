---
name: github-release
description: Use this skill when the user asks to create a GitHub release, publish a release, cut a release, or make a new release of EmoTracker. Handles version bumping, committing, and triggering the release workflow via a git tag.
---

# GitHub Release

Use this skill to publish a new GitHub release of EmoTracker. The `.github/workflows/release.yml` workflow handles building, packaging, and publishing — this skill bumps the version, commits, and pushes the tag that triggers it.

Follow every step in order — do NOT skip steps or assume defaults.

## Step 1: Interview the user

Before doing anything else, ask the user for the following (use the AskUserQuestion tool if available, otherwise ask directly):

1. **Version number** — The new version in `Major.Minor.Build.Revision` form (e.g. `3.0.2.0`).
2. **Prerelease** — Is this a prerelease? (yes/no)

Do not proceed until you have both answers.

## Step 2: Update assembly versions

Update `AssemblyVersion` and `AssemblyFileVersion` to the new version in ALL of these files:

- `EmoTracker/Properties/AssemblyInfo.cs`
- `EmoTracker.UI/Properties/AssemblyInfo.cs`
- `EmoTracker.Data/Properties/AssemblyInfo.cs`
- `EmoTracker.Core/Properties/AssemblyInfo.cs`

Both attributes in each file should be updated:
```csharp
[assembly: AssemblyVersion("X.Y.Z.W")]
[assembly: AssemblyFileVersion("X.Y.Z.W")]
```

After editing, build to confirm it's clean:
```
dotnet build EmoTracker/EmoTracker.csproj
```
Abort and report the error if the build is not clean (0 errors).

## Step 3: Commit, push, and tag

Commit the version changes with:
```
Bump assembly versions to X.Y.Z.W
```
Include the standard `Co-Authored-By: Claude Opus 4.6 <noreply@anthropic.com>` trailer. Always use a HEREDOC for the commit message.

Push the commit to the current branch:
```bash
git push origin HEAD
```

Then create and push the release tag. The tag format determines whether the release workflow triggers:
- Stable release: `vX.Y.Z.W`
- Prerelease: `vX.Y.Z.W-preview`

```bash
git tag vX.Y.Z.W          # or vX.Y.Z.W-preview
git push origin vX.Y.Z.W  # or vX.Y.Z.W-preview
```

## Step 4: Wait for the release workflow

Poll for the workflow run triggered by the tag push. Look for a run on the **Release** workflow (not Build):

```bash
gh run list --limit 10
gh run watch <run-id>
```

If the run fails, report the failure to the user and stop. Do not proceed on a failed run.

## Step 5: Update release notes and mark prerelease

The release workflow creates the GitHub release with placeholder notes. After the workflow completes, replace them with a meaningful summary:

1. Find the previous release tag:
   ```bash
   gh release list --limit 5
   ```

2. Get the commit log since the previous release (excluding the version bump commit itself):
   ```bash
   git log <previous-tag>..HEAD --oneline
   ```

3. Write a concise, human-readable summary of the changes. Group related commits together and omit noise (version bumps, CI tweaks) unless they're notable. Use bullet points.

4. Update the release notes:
   ```bash
   gh release edit <tag> --notes "<your summary>"
   ```

5. If this is a prerelease, also mark it:
   ```bash
   gh release edit <tag> --prerelease
   ```

Steps 4 and 5 can be combined into a single `gh release edit` call.

## Step 6: Report the release URL

```bash
gh release view vX.Y.Z.W[-preview] --json url --jq .url
```

Report the URL to the user.

## Important notes

- Never skip the interview step.
- Never push the tag before confirming the commit was pushed successfully.
- Never proceed past a failed build (step 2) or a failed workflow run (step 4).
- Match the existing commit style: `Bump assembly versions to 3.0.1.0`.
