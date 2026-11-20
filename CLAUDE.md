# DnD-Roller: notes for Claude

Build, test and release recipes, decisions and gotchas, kept in the repo so every computer has them.

DnD-Roller started as a ChatGPT-made single-file WinForms app (v1.0.0, kept as a release) and was redesigned by Claude on 2026-11-15 (v2.0.2). Repo: github.com/DontGoToCollege/DnD-Roller (public).

- One source file `DnDRoller.cs`, compiled by `Build-DnDRoller.ps1` with Windows' built-in csc (C# 5 only: no `$""`, `?.`, `nameof`). `Version` const in the .cs drives the archive name.
- **Use `Build-DnDRoller.ps1 -Test` while iterating.** A plain build archives immediately and archived builds can't be replaced; on 2026-11-15 this forced 2.0.0 and 2.0.1 to be published as "superseded" pre-releases.
- The build runs `DnDRoller.exe --selftest` (parser asserts) and fails on errors.
- Screenshots: never use SendKeys (the app may not be in front, so keys go to whatever window is, e.g. Discord). Use PrintWindow plus PostMessage to the app's focused control, from a DPI-aware PowerShell process.
- Saved rolls and history go to `DnDRoller-saved.txt` / `DnDRoller-history.txt` next to the exe (AppData fallback).

See user-testing-preference.
