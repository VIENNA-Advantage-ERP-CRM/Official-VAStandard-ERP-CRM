# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository scope

The working directory is the **VAS** (VIENNA Advantage Standard) area project — one of four projects in the parent `ViennaAdvantageWeb.sln`:

- `ViennaAdvantageWeb/` — the host ASP.NET MVC web application (Global.asax, Web.config, shared `Dll/` of platform binaries: `VIS.dll`, `VAModelAD.dll`, `CoreLibrary.dll`, `BaseLibrary.dll`, `VISLogic.dll`, etc.).
- `ModelLibrary/` — domain models, callout server logic, and process/report classes shared across areas.
- `VAS/` *(this directory)* — MVC Area named `VAS` containing controllers, views, JS forms/widgets, and CSS for the Standard module.
- `VASLogic/` — business-logic class library backing VAS controllers (`Models/`, `Classes/`).

VAS depends on `ModelLibrary` and `VASLogic` via project references, and on the platform DLLs in `../ViennaAdvantageWeb/Dll/`. Target framework: **.NET Framework 4.8**, ASP.NET MVC 5.2.8.

## Build & dev commands

The .csproj wires npm into MSBuild — opening Visual Studio and building VAS will run webpack first, then xcopy the Areas output. To work on the front-end without rebuilding the .NET project:

```bash
npm install            # first time only
npm run build          # webpack (development mode — fast, used as prebuild)
npm run buildPro       # webpack --mode production (minified output for release)
npm run process        # webpack --progress (verbose)
```

Webpack output paths (these are generated — **do not edit by hand**):
- `Areas/VAS/Scripts/dist/VAS.all.min.js`
- `Areas/VAS/Content/VAS.all.min.css`

The csproj **PostBuildEvent** wipes and re-copies `Areas/*` to `..\ViennaAdvantageWeb\Areas`, so the host web app always sees the latest VAS area output after a Visual Studio build. If you only edit JS/CSS, run `npm run build` then manually copy (or rebuild the project) to refresh the host.

There is no test runner configured in this project.

## Front-end architecture

### Webpack entry points (`webpack.config.js`)
Two entries, both versioned at `1.6.9.0`:
- `VAS.all` ← `Areas/VAS/Scripts/src/VASjs.js` — bundles everything in `app/forms/`, `app/widgets/`, `app/tabpanel/`, and `model/` (callouts).
- `VAS` ← `Areas/VAS/Content/src/VAScss.css` — aggregates all CSS via `@import`.

**Adding a new form/widget/callout requires adding an `import` to `Scripts/src/VASjs.js`** — webpack will not pick it up otherwise. CSS for a new widget should be `@import`ed into `Content/src/VAScss.css`.

Webpack pre-deletes `Areas/VAS/Content/VAS.all.min.css` on every build (see top of `webpack.config.js`) before MiniCssExtractPlugin re-emits it. Mode is currently `'development'` for debuggability; switch to `'production'` (or use `npm run buildPro`) for releases.

### JS module pattern
Files follow a self-invoking IIFE pattern attaching to globals:

```js
; VAS = window.VAS || {};   // or VIS for platform-wide widgets
; (function (VAS, $) {
    VAS.SomeForm = function () { /* ctor */ };
})(VAS, jQuery);
```

`VIS.Application.contextUrl` is the base URL for AJAX; standard endpoints look like `JsonData/JDataSetWithCode`. Forms are instantiated by the platform shell (the `VIS` framework, not in this repo) when a window/form ID maps to the class name.

### User-facing labels — always translatable

**Never hard-code visible text in JS/HTML/templates.** Every label, title, subtitle, button caption, empty-state message, tooltip, helper/footer text, and pluralizable fragment that a user reads must come from the platform's message catalog via:

```js
VIS.Msg.getMsg("VAS_SomeKey")
```

This applies to **every** string the user sees, including microcopy ("WHY", "of", "No data"), bucket/row labels built from data arrays (e.g. an aging-bucket list in `VAS_AgingReceivablesWidget.js`), and any text composed in JS template strings before being injected into the DOM.

Conventions:
- Message keys for VAS-area widgets/forms use the `VAS_` prefix (e.g. `VAS_BankBalance`, `VAS_NoData`, `VAS_Of`). Match the prefix of an existing nearby file when adding new keys.
- When a label is data-driven (a list of bucket descriptors, status labels, etc.), store the *message key* on the descriptor — not the resolved string — and call `VIS.Msg.getMsg(...)` at render time so language switches re-render correctly:
  ```js
  var BUCKETS = [
      { key: "NotDueAmount",  msgKey: "VAS_NotYetDue",   tone: "success" },
      { key: "Days_1_30",     msgKey: "VAS_Days1_30",    tone: "warn" }
      // ...
  ];
  // at render time:
  $row.find(".label").text(VIS.Msg.getMsg(b.msgKey));
  ```
- Keys must be added to the platform's `AD_Message` table (handled by the back-office team / migration script) before the UI will resolve them. New widgets should ship a list of the keys they introduce so the message rows can be inserted.
- Do not concatenate translated fragments to build a sentence — use a single key with placeholders, or compose at render time with separate spans, so RTL/word-order languages still read correctly.
- Existing widgets like `VAS_BankBalanceWidget.js` (`VIS.Msg.getMsg("VAS_BankBalance")`, `VIS.Msg.getMsg("VAS_NoData")`, `VIS.Msg.getMsg("VAS_Of")`) are the reference pattern.

When editing a file that still contains hard-coded user-facing strings, replace them as you go rather than adding more.

### Bundle registration (`VASAreaRegistration.cs`)
Registers the single combined bundle with the platform's `VAdvantage.ModuleBundles` (priority `-9`):
- StyleBundle → `~/Areas/VAS/Content/VAS.all.min.css`
- ScriptBundle → `~/Areas/VAS/Scripts/dist/VAS.all.min.js`

The large commented-out `Include(...)` lists are historical (pre-webpack manual bundling) — leave them; replacing them with the webpack-built min files is intentional.

## Server-side architecture

### Routing
Single area route in `VASAreaRegistration.cs`: `VAS/{controller}/{action}/{id}`.

### Controllers (`Areas/VAS/Controllers/`)
Two flavors:
- **Top-level** (e.g. `OrderController.cs`, `PaymentController.cs`, `VAS_LeadController.cs`) — page/form endpoints returning `JsonResult` or views.
- **`CallOut/` subfolder** (`MOrderController.cs`, `MProductController.cs`, ...) — server-side callouts paired with the client-side callouts in `Scripts/model/` (e.g. `calloutorder.js` ↔ `MOrderController.cs`). Callouts compute dependent field values when a user changes a field on a form.

Controllers consistently:
- Pull session context with `var ctx = Session["ctx"] as Ctx;` (the `Ctx` type is from `VAdvantage` in `VIS.dll`).
- Delegate to a model class in `VASLogic/Models/` or `ModelLibrary/`.
- Return `Json(JsonConvert.SerializeObject(...), JsonRequestBehavior.AllowGet)`.

Namespaces: top-level files frequently sit in `namespace VIS.Controllers` (not `VAS.Controllers`) because the platform's view/script lookup expects that — match the namespace of an existing nearby file when adding a new controller rather than guessing.

### SQL & data access
- Use `VAdvantage.DataBase` helpers (`DataBase.DB.ExecuteQuery`, etc.) — these come from the platform DLLs.
- **Always use parameterized SQL.** Recent commits (e.g. `a3ef7990 handled sql injection by using sql parameters`, branches named `VAI147_SqlParam`) explicitly remediated injection bugs by switching from string concatenation to `SqlParameter[]`. New code must follow the parameterized pattern from neighboring controllers/models.

## File-naming conventions

- `VAS_*` prefix → custom Standard-module additions (widgets, forms, controllers added by the VAS team).
- `M*Controller.cs` inside `Controllers/CallOut/` → server-side callouts mirroring a `Scripts/model/callout*.js`.
- Comment headers in JS often carry an employee code (e.g. `VAI061`, `VIS316`) and dates — preserve the existing header style when editing those files.

## What lives outside this directory but matters

- **Platform DLLs** in `../ViennaAdvantageWeb/Dll/` are referenced as binary `<HintPath>` entries — they are not source-available here. Don't try to "go to definition" into them; check method signatures via the `Dll/` PDBs or rely on usage patterns in existing code.
- **Web.config, Global.asax, App_Start** all live in `../ViennaAdvantageWeb/`. The `Web.config` change you might see in `git status` originates from the host project, not VAS.
- **Other areas**: `../ViennaAdvantageWeb/Areas/VIS` and `../ViennaAdvantageWeb/Areas/ViennaBase` are the platform's own areas and contain shared client-side framework JS/CSS that `VAS.*` widgets call into (`VIS.Application`, `VIS.Env`, `VIS.Msg`, etc.).

## Message Deduplication & Key Reuse
- Before adding any message to the `.xlsx` file, check whether the message text matches an existing entry in the `AD_Message` table's `msgtxt` column.
- If a match is found **and** the existing message key has either:
  - **No prefix**, or
  - A prefix starting with `VIS_` or `AD_`
  
  then:
  - **Reuse the existing message key** — do **not** add a new message entry to the `.xlsx` file.
  - **Replace the message key value in the project** wherever the new/duplicate key is referenced, so the project points to the existing `AD_Message` key instead.
- Also, if the message text already exists anywhere in the current project, do not add it again — reuse the existing project key and update any references accordingly.

## Database Query Security
- All queries **must** be wrapped in `MRole` security.
- Always follow SQL injection prevention best practices:
  - Use parameterized queries / prepared statements.
  - Never concatenate user input directly into SQL strings.
  - Validate and sanitize all inputs before use in queries.

## Additional Context
See @design.md for [Onfinity Dashboard Design System].