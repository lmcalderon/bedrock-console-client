---
name: release-notes
description: >-
  Write release notes and changelog entries for this project's GitHub
  releases. Use when preparing a release, drafting a tag's release notes,
  or summarizing a milestone for whoever might run this client.
metadata:
  category: discipline
  triggers:
    - release notes
    - changelog
    - cut a release
    - tag a release
    - release version
---

# Release Notes

Adapted from [tilomitra/release-kit-claude-skills](https://github.com/tilomitra/release-kit-claude-skills) (MIT licensed) — its core discipline (write for the reader, omit anything without clear user impact, never mention file/function names) transfers directly; the parts assuming a PR-driven SaaS workflow and a multi-tier audience don't, and are adapted below for this project's actual shape (direct commits to `main`, one audience: whoever runs this client).

## The one rule that matters most

Release notes are for the person deciding whether and how to use this release — not a development journal. Implementation detail, the debugging story, bugs found and fixed along the way: none of that belongs here even when it was the most interesting part of doing the work. It already has a home in `docs/notes/`. If a change has no effect on someone running the client, leave it out entirely — don't include it just because it took real effort.

This came up for real drafting v0.1.0's notes: a "bugs caught during implementation" section got written, then correctly cut, because "the AES-CTR keystream wasn't a continuous stream" means nothing to someone deciding whether to run this client.

## Gathering context

This project commits directly to `main` (no PR workflow), so PRs/issues aren't a source here. Use:

```bash
# Commits since the last tag (or since the beginning, for the first release)
git log --oneline <previous-tag>..HEAD

# What actually changed
git diff <previous-tag>..HEAD --stat

# Design docs written or updated in this range often say more than the
# commits do about what actually changed and why
git diff <previous-tag>..HEAD -- docs/notes/
```

If this repo ever starts using PRs, merged-PR titles/descriptions are usually more useful than raw commit messages — add that step back in then, not preemptively now.

## Filtering: what's in, what's out

- **In**: anything that changes what the client does, how it's configured, or what it needs to run.
- **Out**: internal refactors, test changes, tooling/lint config, the debugging path taken to get somewhere (that's `docs/notes/` material) — unless a bug is a real known limitation in *this* release, not just something that got fixed before shipping.
- If you can't articulate the effect on someone running the client in one sentence, leave it out.

## Writing rules

- Write for someone running the client, not for a developer reading a diff: "server address and username are now configurable" not "added `BedrockClientConfigLoader`."
- Active voice: "adds X" not "X was added."
- Never mention file names, class names, or internal implementation details — that's what `docs/notes/` is for, link to it instead of restating it.
- No hype. This isn't a SaaS product changelog — skip "exciting," "powerful," anything that reads as trying to sell the reader on the release. State what's true; let it stand on its own. Run the actual text through the `humanizer` skill's checklist before finalizing, particularly the promotional-language and em-dash/dash-clause patterns — draft passes have repeatedly needed a cleanup pass for exactly this.
- Be honest about scope. "Works against offline-mode local/third-party servers; no runtime config for Xbox Live yet" beats overclaiming or underclaiming what's actually done.

## Versioning convention for this project

Pre-1.0, each completed milestone is a minor version bump: `v0.1.0` for Milestone 1, `v0.2.0` for Milestone 2, and so on — semver's own convention for "initial development, anything may still change." Tag format: `vX.Y.Z`, annotated.

## Output format

```markdown
## Milestone N: <what it does now, in a few words>

<One or two sentences: what the client can do after this release that it
couldn't before.>

### What works
- <User-visible capability, one line each>

### Not in this release
<Explicit scope boundary — what's deliberately not here yet, and why if it's not obvious.>
```

Point to `docs/notes/*.md` for anyone who wants implementation depth, rather than summarizing it inline.

## Publishing

```bash
git tag -a vX.Y.Z -m "vX.Y.Z"
git push origin main --tags
gh release create vX.Y.Z --title "vX.Y.Z" --notes-file release-notes.md
```

Confirm the tag and release content with the user before pushing or creating the release — both are visible, public actions.
