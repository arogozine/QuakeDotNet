// SPDX-License-Identifier: GPL-2.0-only
// Copyright (C) 2026 Alexandre Rogozine

// Every test in this assembly runs strictly one after the other. Nearly every
// test class shares Quake.Engine's mutable static state (Com, Hunk, Cache,
// Console, Model, ...), so no two tests may ever overlap -- not even from
// different classes. This single assembly-level attribute is what enforces
// it; test classes don't need (and shouldn't add) their own
// [DoNotParallelize]. See EngineStateGuard.cs for how state is reset
// *between* those sequential tests.
[assembly: DoNotParallelize]
