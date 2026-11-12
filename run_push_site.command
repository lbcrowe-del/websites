#!/bin/bash
cd "$HOME/Projects/websites" || exit 1
echo "Current status:"
git status
echo ""
echo "Pushing to origin/main..."
git push origin main
echo ""
read -p "Press Enter to close this window..."
