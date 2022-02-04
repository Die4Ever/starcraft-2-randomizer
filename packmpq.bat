if not exist sc2randomizer.SC2Mod\BankList.xml (
    echo "BankList.xml must exist"
    pause
    exit /b 1
)

del /f %TMP%\sc2randomizer.SC2Mod
if exist %TMP%\sc2randomizer.SC2Mod (
    echo "%TMP%\sc2randomizer.SC2Mod still exists!"
    pause
    exit /b
)

MPQEditor.exe /new %TMP%\sc2randomizer.SC2Mod
MPQEditor.exe /add %TMP%\sc2randomizer.SC2Mod sc2randomizer.SC2Mod\ /r
REM MPQEditor.exe /flush %TMP%\sc2randomizer.SC2Mod
REM MPQEditor.exe /compact %TMP%\sc2randomizer.SC2Mod
REM MPQEditor.exe /list %TMP%\sc2randomizer.SC2Mod
dir %TMP%\sc2randomizer.SC2Mod
start %TMP%\sc2randomizer.SC2Mod
