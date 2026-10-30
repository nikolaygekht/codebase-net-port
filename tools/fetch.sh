#!/bin/sh
# ===========================================================================
#  fetch.sh — clone, or update to the latest commit, the two quality tools
#  from Microsoft's repositories into tools/, and build them. The clones are
#  gitignored; tools/README.md says how to run them.
#
#  Prerequisites: git, and the .NET 8 SDK the tools' global.json asks for.
#  fetch.bat does the same on Windows.
# ===========================================================================
set -e

cd "$(dirname "$0")"

for pair in crap4csharp:Crap4CSharp mutate4csharp:Mutate4CSharp; do
    repo=${pair%%:*}
    project=${pair#*:}

    if [ -d "$repo/.git" ]; then
        echo "[fetch] updating $repo"
        git -C "$repo" pull --ff-only
    else
        echo "[fetch] cloning $repo"
        git clone "https://github.com/microsoft/$repo.git" "$repo"
    fi
    git -C "$repo" log -1 --format="[fetch] $repo at %h %ad %s" --date=short

    # Built from inside the clone, so its own global.json picks the SDK.
    (cd "$repo" && dotnet build "src/$project/$project.csproj" -c Release --nologo -v quiet)
done

echo "[fetch] done"
