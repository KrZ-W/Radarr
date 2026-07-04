# Releasing (maintainer notes)

How to cut a versioned release of the KrZ-W/Radarr fork. See
[../FORK.md](../FORK.md#versioning) for the versioning scheme.

## Version format recap

```
git tag / GitHub release :  v<upstream-version>+krzw.<N>     e.g. v6.2.1.10461+krzw.1
docker image tag         :  <upstream-version>-krzw.<N>      e.g. 6.2.1.10461-krzw.1
```

- `<upstream-version>` = the Radarr version `personal/all-features-master` is rebased
  onto. Confirm it with:

  ```bash
  git describe --tags --abbrev=0 --match 'v*' \
    "$(git merge-base upstream/develop personal/all-features-master)"
  ```

- `<N>` starts at `1` for each new upstream base and increments for subsequent fork
  releases on that **same** base. After a rebase onto a newer upstream, reset to `1`.

## Steps

1. **Make sure `personal/all-features-master` is in the state you want to ship** and
   the image builds (the `docker-image.yml` workflow already builds branch pushes).

2. **Update `CHANGELOG.md`:**
   - Move the entries under `[Unreleased]` into a new
     `## [v<ver>+krzw.<N>] — based on Radarr <upstream-version>` section.
   - Reset `[Unreleased]` to `_Nothing yet._`.
   - Update the two link-reference lines at the bottom of the file.

3. **Commit** the changelog (and any doc updates):

   ```bash
   git commit -am "docs: release v6.2.1.10461+krzw.1"
   git push myfork personal/all-features-master
   ```

4. **Tag and push the tag.** The `+` is fine in a git tag:

   ```bash
   git tag -a 'v6.2.1.10461+krzw.1' -m 'Fork release based on Radarr 6.2.1.10461'
   git push myfork 'v6.2.1.10461+krzw.1'
   ```

   This triggers `docker-release.yml`, which builds and pushes the immutable image tag
   `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.1` (it maps `+` → `-` automatically).

5. **Boot-test the release image before announcing it.** A green CI build is not
   proof the image runs — the Sonarr fork's `v4.0.19.2979+krzw.1` image built green
   but crash-looped in production:

   ```bash
   docker pull ghcr.io/krz-w/radarr:6.2.1.10461-krzw.1
   docker run -d --name radarr-boot-test -p 17878:7878 ghcr.io/krz-w/radarr:6.2.1.10461-krzw.1
   sleep 20
   curl -sf http://localhost:17878/ping    # expect {"status":"OK"}
   docker rm -f radarr-boot-test
   ```

   If the container restarts or `/ping` fails, fix and cut the next `krzw.<N+1>` —
   never announce an image that hasn't booted.

6. **Create the GitHub release** from the tag, using the changelog section as the body:

   ```bash
   gh release create 'v6.2.1.10461+krzw.1' \
     --repo KrZ-W/Radarr \
     --title 'v6.2.1.10461+krzw.1' \
     --notes-file <(sed -n '/## \[v6.2.1.10461+krzw.1\]/,/## \[/p' CHANGELOG.md | sed '$d')
   ```

   (Or paste the changelog section into the web UI.)

## After rebasing onto a newer upstream

1. Rebase each `feature/*` / `fix/*` branch onto the new `upstream/develop`, re-merge
   into `personal/all-features-master`, resolve conflicts.
2. Re-confirm the new `<upstream-version>` with the `git describe` command above.
3. Refresh the `## Source` commit hashes in `docs/features/*.md` — a rebase rewrites
   every fork commit, so the cited hashes go stale. Find the new ones with
   `git log --oneline <upstream-base>..HEAD`. Update the example versions in this file
   and in `FORK.md` at the same time.
4. Add an `[Unreleased]` → new-version section noting the rebase, then release as
   `v<new-upstream-version>+krzw.1`.

## CI overview

| Workflow | Trigger | Produces |
|---|---|---|
| `docker-image.yml` | push to `personal/**`, `feature/**`, `fix/**` | `:latest` (primary branch), `:<branch>`, `:sha-<short>` |
| `docker-release.yml` | push of a `v*` tag | `:<upstream-version>-krzw.<N>` (immutable release image) |
