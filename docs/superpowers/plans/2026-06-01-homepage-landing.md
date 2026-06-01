# Homepage Landing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the approved homepage landing page and setup guide.

**Architecture:** Keep the implementation in Razor Pages with page-local CSS, matching the existing WebApp pattern. Modify only public navigation and public pages.

**Tech Stack:** .NET 8 Razor Pages, existing CSS variables in `wwwroot/app.css`, existing OpenRussiaRouter WebApp routes.

---

### Task 1: Navigation

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Shared/_Layout.cshtml`

- [x] **Step 1: Remove Pricing from top navigation**

Delete the `/pricing` link because there is no public pricing page in the current WebApp.

- [x] **Step 2: Rename Docs to Setup guide**

Change the `/docs` link text to `Setup guide` and use the existing active-state helper.

### Task 2: Homepage Landing

**Files:**
- Modify: `src/OpenRussiaRouter.WebApp/Pages/Index.cshtml`

- [x] **Step 1: Replace the minimal homepage with the landing structure**

Add hero, primary CTAs, four-step quickstart, catalog proof, billing proof, routing proof, request example, and final CTA.

- [x] **Step 2: Add page-local responsive CSS**

Use the existing design tokens, stack grids below 900px, and make CTA rows full-width below 640px.

### Task 3: Setup Guide

**Files:**
- Create: `src/OpenRussiaRouter.WebApp/Pages/Docs.cshtml`

- [x] **Step 1: Create the `/docs` Razor page**

Add a compact setup guide with the same four-step flow and an OpenAI-compatible `/v1/chat/completions` curl example.

### Task 4: Verification

**Files:**
- Verify: `OpenRussiaRouter.sln`
- Modify: `src/OpenRussiaRouter.WebApp/wwwroot/app.css`

- [x] **Step 1: Build the solution**

Run: `dotnet build OpenRussiaRouter.sln --no-restore -nr:false -m:1`

Expected: Build succeeds without Razor compilation errors.

- [x] **Step 2: Fix mobile shell placement**

Set `.main-pane` to `grid-column: 1` inside the existing `max-width: 768px` media query so public pages do not render off-canvas when the sidebar is hidden.

- [x] **Step 3: Inspect git diff**

Run: `git diff -- src/OpenRussiaRouter.WebApp/Pages/Index.cshtml src/OpenRussiaRouter.WebApp/Pages/Shared/_Layout.cshtml src/OpenRussiaRouter.WebApp/Pages/Docs.cshtml src/OpenRussiaRouter.WebApp/wwwroot/app.css`

Expected: Diff is limited to the approved landing page, setup guide, and nav changes.
