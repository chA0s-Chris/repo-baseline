// Copyright (c) 2026 Christian Flessa. All rights reserved.
// This file is licensed under the MIT license. See LICENSE in the project root for more information.
#:project ../src/RepoBaseline/RepoBaseline.csproj
#:property TargetFramework=net10.0
#:property RollForward=Major

// repo-baseline tool: `init` sets up a repository from the templates and synced content of a release,
// `sync` writes the synced content of a pinned release into a repository, and `check` reports where that
// content has been changed locally. The code is in src/RepoBaseline; docs/design.md describes profiles,
// templates, managed blocks and the manifest; the README describes how to run the tool.

return Chaos.RepoBaseline.Cli.Run(args);
