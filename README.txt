D&D DICE ROLLER
===============

A small, local, offline Windows dice roller. No network access, accounts, installer,
third-party libraries, or data collection.

FIRST RUN
1. Extract this folder anywhere (USB drive is fine).
2. Open Windows PowerShell in this folder.
3. Run: .\Build-DnDRoller.ps1
4. Double-click DnDRoller.exe thereafter.

The executable targets the .NET Framework already included with supported Windows
installations. The build script uses the Windows PowerShell/.NET Framework compiler.

FEATURES
- Quick buttons for d4, d6, d8, d10, d12, d20, and d100
- Custom dice expressions, e.g. 2d6+3, d20-1, 4d8+2d6+5
- Advantage and disadvantage for a single d20 check
- Dice count and modifier controls
- Roll breakdown and session history
- No network use; history is kept only in memory and cleared on exit

NOTES
- Build once on Windows to produce DnDRoller.exe in this folder.
- Keep the EXE by itself for portable use; the .NET Framework is part of Windows.
