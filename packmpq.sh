#!/bin/bash
set -e

# Set TMP directory
TMPDIR="./tmp"
(mkdir "$TMPDIR" > /dev/null 2>&1) || true

# Check if BankList.xml exists
if [ ! -f "sc2randomizer.SC2Mod/BankList.xml" ]; then
    echo "BankList.xml must exist"
    read -p "Press enter to continue..."
    exit 1
fi

# Remove temporary SC2Mod file
TMP_MOD="$TMPDIR/sc2randomizer.SC2Mod"
if [ -f "$TMP_MOD" ]; then
    rm -f "$TMP_MOD"
fi

# Check again if it still exists
if [ -f "$TMP_MOD" ]; then
    echo "$TMP_MOD still exists!"
    read -p "Press enter to continue..."
    exit 1
fi

# Run MPQEditor commands
wine MPQEditor.exe /new "$TMP_MOD"
wine MPQEditor.exe /add "$TMP_MOD" "sc2randomizer.SC2Mod/" /r

# Show the created file
ls -l "$TMP_MOD"

# manually open SC2 map editor using Lutris
FULL=$(pwd "$TMP_MOD")
echo "SUCCESS! Use Lutris to open the SC2 Map Editor and then open $FULL (this might show up as the S:\\ or X:\\ drive)"
