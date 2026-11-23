# Test Cases — Changes Since Release 1.7.3.0_1

**Repository:** VIENNA-Advantage-ERP-CRM/Official-VAStandard-ERP-CRM
**Comparison:** [`1.7.3.0_1...master`](https://github.com/VIENNA-Advantage-ERP-CRM/Official-VAStandard-ERP-CRM/compare/1.7.3.0_1...master)
**Target release:** 2.0.1.0
**Prepared for:** QA validation of changes attributed to Bharat-Vienna
**Status:** Proposed test coverage; execute in a non-production environment.

## Test execution guidance

For each test, record the environment, database type, tenant, user/role, module configuration, test data, actual result, and pass/fail status. Confirm data changes and accounting/costing results using the application UI and approved reports.

## 1. Costing and inventory performance

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| COST-001 | FIFO receipt and issue | Configure an item for FIFO; complete a receipt, then an issue. | Cost queue and cost details are created; issue cost follows FIFO order and quantity balances are correct. |
| COST-002 | LIFO receipt and issue | Configure an item for LIFO; complete multiple receipts and an issue. | Cost queue and cost details are created; issue cost follows LIFO order. |
| COST-003 | Costing without FIFO/LIFO configuration | Process a product using a non-FIFO/LIFO costing method. | Costing succeeds using the configured method; no invalid FIFO/LIFO element is selected. |
| COST-004 | First-row-only FIFO/LIFO query | Create several open queue rows; run costing that only needs the current queue price. | Correct first queue row is used; results match normal costing calculations. |
| COST-005 | Queue/detail direct insert | Complete receipt, shipment, invoice, movement, and inventory transactions that generate costing rows. | Required cost queue, queue transaction, cost detail, and temporary detail records contain correct IDs, quantities, amounts, dates, and references. |
| COST-006 | Optional module columns absent/present | Repeat costing with optional asset/manufacturing modules absent, then installed with corresponding columns. | Costing succeeds in both configurations; optional references are written only when available. |
| COST-007 | Accounting-schema lookup cache | Cost several lines for a document with multiple accounting schemas. | Each line uses the correct schema, precision, and currency; caching does not mix schema data. |
| COST-008 | Client lookup cache expiry | Use the costing cache, change a client-level costing configuration, then retry after the five-minute cache lifetime. | Updated configuration is used after expiry. |
| COST-009 | Material cost combination cache | Process repeated cost combinations, including a combination with no linked material element. | Repeated combinations produce the same cost as uncached processing; missing links do not cause failures. |
| COST-010 | Cost queue product/client filters | Process cost details for multiple products and clients. | Only matching client/product queue transactions affect the result. |
| COST-011 | Same-product concurrent costing | Submit independent costing work for the same product concurrently. | Calls serialize for that product; no duplicate, lost, or inconsistent cost queue updates occur. |
| COST-012 | Different-product concurrent costing | Submit costing work for distinct products concurrently. | Work can proceed concurrently; results for each product remain correct. |
| COST-013 | Exclusive costing operation | Run landed-cost or charge/expense costing while product costing is active. | Exclusive operation waits safely and completes without cross-product cost corruption. |
| COST-014 | Costing failure/rollback | Trigger an insert or validation failure during costing. | Error is logged/reported and transactional costing rows are not left partially committed. |
| COST-015 | Costing regression/performance | Compare representative multi-line documents with an approved baseline. | Business totals and cost results are unchanged; performance does not regress. |

## 2. Payment allocation and reconciliation

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| PAY-001 | One payment allocated to multiple invoices | Allocate one payment across several invoices and complete the allocation. | Payment is validated once and all invoice paid amounts and allocation totals are correct. |
| PAY-002 | Cash line shared across allocation lines | Allocate a cash line across multiple invoices. | Cash line is loaded/reused correctly; invoice and cash/order links are correct. |
| PAY-003 | Invoice with linked sales order | Allocate payment to an invoice associated with an order. | Invoice and order receive the correct payment link. |
| PAY-004 | Invoice without linked order | Allocate payment to an invoice with no order. | Allocation completes without attempting an invalid order update. |
| PAY-005 | Already-paid invoice schedule | Allocate against a schedule already marked paid. | Schedule is not double-paid; allocation handling follows existing rules. |
| PAY-006 | Reverse allocation | Reverse a completed allocation involving payment and cash lines. | Allocation links and paid amounts are reversed correctly. |
| PAY-007 | Allocation rollback | Cause a controlled validation failure during completion. | No partial payment, invoice, schedule, or order changes remain. |
| PAY-008 | Parameterized payment-selection query | Run payment selection with ordinary and special-character payment-rule values. | Correct checks are returned without query parsing errors. |
| PAY-009 | Void bank statement with payment | Void a reconciled statement containing a payment. | Payment reconciliation is reset. |
| PAY-010 | Void bank statement with cash line | With the cash module enabled, void a statement containing a cash line. | Cash-line reconciliation flag is reset to `N`. |
| PAY-011 | Void bank statement without optional module | Repeat with the optional cash module unavailable. | Void succeeds without trying to update unavailable module data. |
| PAY-012 | Cash-line reset failure | Make the cash-line update fail in a test environment. | Failure is logged and no silent incorrect success is reported. |

## 3. Invoice, tax, and payment-term behavior

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| INV-001 | Tax-to-surcharge change | Change an invoice line from surcharge tax to another tax and recalculate. | Previous surcharge amount is cleared; tax and totals are correct. |
| INV-002 | Surcharge recalculation | Change surcharge inputs and recalculate invoice tax. | Surcharge and invoice totals match configured tax rules. |
| INV-003 | Manual vendor tax | Create a vendor invoice with manually entered tax. | Manual tax is preserved without incorrect override. |
| INV-004 | Price with attribute set instance | Price an invoice line with a selected attribute set instance. | Pricing uses the line's current attribute set instance. |
| INV-005 | Converted entered price | Create a line where price has already been converted. | Entered price remains equal to actual price and is not incorrectly recalculated. |
| INV-006 | Invoice with many lines | Complete a multi-line invoice with repeated products and schemas. | Header totals, taxes, cost details, and transaction references are correct. |
| INV-007 | Invoice reversal | Reverse an invoice with original cost details. | Reversal costing follows original document/reversal rules. |
| INV-008 | Revenue recognition absent | Complete an invoice where recognition reference is null/zero. | Invoice completes without a recognition plan. |
| INV-009 | Revenue recognition configured | Complete an eligible invoice line with a valid recognition ID. | Recognition plan/run is created once. |
| INV-010 | Payment schedule copy with null optional fields | Copy a schedule where optional payment fields are empty. | Empty values do not overwrite existing schedule values. |
| INV-011 | Payment schedule copy with populated optional fields | Copy a schedule with all optional values populated. | Populated fields and cash-line reference are copied correctly. |
| INV-012 | Date at accounting-period boundary | Look up period using date/time including period start/end dates. | Period lookup uses the date portion and returns the correct period. |

## 4. Shipment, order, and invoice-from-shipment behavior

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| DOC-001 | Complete standard customer shipment | Complete a shipment with item products and sufficient stock. | Shipment completes; stock, transactions, and costing are correct. |
| DOC-002 | Negative stock prohibited | In a warehouse disallowing negative stock, ship more than available. | Completion is blocked/invalid and stock is unchanged. |
| DOC-003 | Negative stock allowed | Repeat in a warehouse allowing negative stock. | Shipment follows configured negative-stock behavior. |
| DOC-004 | Vendor receipt PO matching | Complete a non-reversal vendor receipt linked to a purchase order. | Availability and PO matching information are calculated correctly. |
| DOC-005 | Customer shipment skips PO-only availability query | Complete a customer shipment. | Shipment completes normally; no PO matching calculation affects it. |
| DOC-006 | Non-item products disallowed | Generate shipment from an order containing charge/service/expense lines while tenant setting disallows non-items. | Shipment line creation follows the setting. |
| DOC-007 | Non-item products allowed | Repeat with tenant setting enabled. | Non-item shipment behavior follows the setting. |
| DOC-008 | Sales quotation line deletion | Delete an allowed line from a sales quotation that is not draft. | Quotation line deletion succeeds under the quotation-specific rule. |
| DOC-009 | Regular processed order line deletion | Attempt to delete a nonzero line from a processed non-quotation order. | Deletion remains blocked with the expected message. |
| DOC-010 | Invoice from shipment marks lines invoiced | Generate an invoice from a completed shipment. | Selected shipment lines are marked invoiced; invoice totals/tax are recalculated correctly. |
| DOC-011 | Invoice-from-shipment rollback | Cause invoice creation failure during processing. | Shipment lines are not left incorrectly marked invoiced. |
| DOC-012 | Order history references | Complete an order and inspect order-line history rows. | History rows include the correct `C_Order_ID`. |

## 5. BOM costing

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| BOM-001 | Add active BOM detail | Add a detail with a current cost. | Header BOM cost equals the sum of active detail costs. |
| BOM-002 | Update BOM detail | Change an active detail cost and save. | Header BOM cost recalculates to the new total. |
| BOM-003 | Delete BOM detail | Delete an active detail. | Header BOM cost decreases to the sum of remaining active details. |
| BOM-004 | Ignore inactive detail | Deactivate a detail and save/recalculate. | Inactive detail is excluded from header cost. |
| BOM-005 | Semi-finished child BOM | Add an SFG component with a child BOM. | Component cost rolls up child BOM cost plus labour overhead. |
| BOM-006 | Empty child BOM | Use an active child BOM with no active details. | Component cost safely resolves to zero or the configured fallback. |
| BOM-007 | No-op update | Save a detail without changing costs. | Header is not unnecessarily changed; cost values remain correct. |

## 6. Opportunity, lead, prospect, quotation, and order

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| CRM-001 | Create opportunity from customer lead | Convert a lead linked to a customer. | Opportunity is created with source lead, partner, location, campaign, sales rep, and contact. |
| CRM-002 | Create opportunity from prospect lead | Convert a lead linked to a prospect. | Prospect/agent and source links are correct on the opportunity. |
| CRM-003 | Create BP from lead then opportunity | Convert a lead with company name and BP group but no existing BP/prospect. | BP is created, then opportunity is created and lead is marked converted. |
| CRM-004 | Lead missing company/BP group | Run conversion with required source information missing. | Clear validation is returned; no partial opportunity is created. |
| CRM-005 | Prospect-to-opportunity from lead | Run process on a prospect associated with a lead. | Opportunity is created and lead/prospect history links are correct. |
| CRM-006 | Prospect-to-opportunity without matching lead | Run process on prospect with no matching lead. | Opportunity is created using available prospect/user data. |
| CRM-007 | Opportunity line defaults | Add a new opportunity line. | Defaults are quantity 1 and zero amount/price/margin as appropriate. |
| CRM-008 | Opportunity line number | Add multiple lines to one opportunity. | Line numbers are unique, ordered, and increment in sequence. |
| CRM-009 | Opportunity line planned amount rounding | Enter quantity/price with excess decimal precision. | Planned amount rounds using price-list currency precision. |
| CRM-010 | Opportunity line planned margin | Change product, quantity, or planned price. | Planned margin recalculates using price limit and currency precision. |
| CRM-011 | Opportunity header planned total | Add, edit, and delete lines. | Header planned amount equals the sum of active line amounts. |
| CRM-012 | Opportunity campaign costs | Save opportunities associated with a campaign. | Campaign costs equal the sum of active opportunity planned amounts. |
| CRM-013 | Generate quotation with product and charge lines | Create opportunity with product and charge lines, then generate quotation. | Quotation has the correct document subtype, partner, price list, currency, quantities, prices, UOM, and attributes. |
| CRM-014 | Quotation generation with missing partner | Generate quotation without customer/prospect. | Process reports missing partner; no quotation is created. |
| CRM-015 | Quotation generation with missing location | Generate quotation without partner location. | Process reports missing location; no quotation is created. |
| CRM-016 | Quotation generation without lines | Generate quotation from an opportunity with no lines. | “No Lines” is reported; no quotation is created. |
| CRM-017 | Quotation link and generation flag | Generate a quotation successfully. | Opportunity `Ref_Order_ID` and generation marker point to the created quotation. |
| CRM-018 | Generate sales order | Create opportunity with valid partner/location/lines and generate order. | Correct order is created and lines/generation marker are correct. |
| CRM-019 | Generate order from prospect | Generate order where prospect is the source partner. | Prospect is converted/used as customer as intended. |
| CRM-020 | Order/quotation back-link | Generate quotation/order where order has opportunity column. | Order references the opportunity; opportunity ID is not written into `C_Project_ID`. |
| CRM-021 | Document tax totals | Generate quotation/order with taxes. | Tax totals and header totals are calculated once and match expected values. |
| CRM-022 | Archive opportunity | Run archive process with a valid reason. | `VAS_IsArchived` is set and reason is saved. |
| CRM-023 | Archive invalid/missing record | Run archive with record ID zero or nonexistent ID. | Process fails with a clear not-found/ID error. |
| CRM-024 | Lead email changed | Change email on an existing lead and save. | Previous user/BP association is cleared and reassociated according to the new email. |
| CRM-025 | Lead next-step validation | Save lead with required next-step missing, then populated. | Missing reference is rejected; populated reference saves successfully. |

## 7. Alerts and reference-value resolution

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| ALERT-001 | Insert alert | Configure an insert rule and create a record in the selected tab. | Email is sent with correct window/tab display names and record values. |
| ALERT-002 | Update alert old/new scalar values | Update a field monitored by an alert rule. | Email shows field name, previous value, and new value. |
| ALERT-003 | Update alert reference values | Change a lookup/list field. | Email shows readable old/new display values, not raw IDs/codes. |
| ALERT-004 | Update unmonitored field | Change a field not in the rule. | No update alert is generated. |
| ALERT-005 | Delete alert | Delete a monitored record. | Alert uses the old record ID and includes available record details. |
| ALERT-006 | Rule tab fallback | Configure a rule with `AD_Tab_ID`; trigger it when PO lacks window/tab context. | Correct window and tab are resolved from tab metadata. |
| ALERT-007 | User recipient with table access | Configure user recipient whose role has table access. | User receives alert. |
| ALERT-008 | User recipient without table access | Configure user recipient whose roles lack table access. | User is excluded. |
| ALERT-009 | Role recipient access | Configure a role recipient with and without table access. | Only users in an authorized role receive the alert. |
| ALERT-010 | Notification SQL context | Use a validated recipient SQL containing context tokens. | Tokens are parsed and recipients are resolved correctly. |
| ALERT-011 | Reference null values | Trigger alert when a lookup/list value is null. | Email renders null display value without exception. |
| ALERT-012 | Window access denied | Run reference resolution under a role without window access. | Access is denied safely; no unauthorized field data is returned. |
| ALERT-013 | Unknown window or missing record | Trigger processing with invalid window/table record. | No email with fabricated values; error is logged. |
| ALERT-014 | Email rendering for each event | Trigger INSERT, UPDATE, DELETE. | Correct subject, columns, old/new values, record ID, and HTML layout are used. |

## 8. Tenant setup and role access

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| TENANT-001 | Create tenant | Run tenant setup with valid client, organization, and admin details. | Tenant and admin user are created successfully. |
| TENANT-002 | Default role naming | Create tenant with active template roles. | Tenant roles are named with tenant suffix; system roles are excluded. |
| TENANT-003 | System user-level role | Include a template role with user level `S`. | Cloned tenant role uses Client+Organization level. |
| TENANT-004 | Access copying | Include active organization, user, window, process, form, workflow, and task access. | Active access records are copied. |
| TENANT-005 | Inactive access exclusion | Add inactive template access records. | Inactive entries are not copied. |
| TENANT-006 | Widget access copying | Include active widget access. | Tenant role has corresponding widget access. |
| TENANT-007 | Dashboard and components | Include active dashboard access and widgets. | Dashboard, role access, and dashboard widgets are copied and linked. |
| TENANT-008 | User organization access | Use role configured for user organization access. | Admin user receives appropriate org and wildcard access. |
| TENANT-009 | Tenant setup failure | Force controlled failure during setup. | Setup rolls back without partial roles/dashboards/access. |

## 9. Email attachment processing

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| MAIL-001 | Auto-attach inbound mail | Enable auto-attach and process inbox mail linked to a known record. | Mail attaches to the expected record. |
| MAIL-002 | Save unlinked inbox mail | Disable auto-attach where configuration permits unlinked storage. | Mail is stored as unlinked attachment. |
| MAIL-003 | Sent mail processing | Process a sent-folder message. | Sent message is stored once with sent-folder/attachment classification. |
| MAIL-004 | Duplicate message processing | Process the same message ID twice. | Duplicate attachment is detected and not inserted again. |
| MAIL-005 | Account metadata | Process an attachment from configured mail account. | Mail account and user account ID are stored correctly. |
| MAIL-006 | AI mail suggestion success | Configure active orchestration and process mail. | Suggestion endpoint is called and success is recognized. |
| MAIL-007 | AI disabled/unconfigured | Disable optional AI module or omit orchestration. | Mail processing continues without missing optional columns or AI calls. |
| MAIL-008 | AI service error/invalid JSON | Simulate failed request or malformed response. | Error is logged; unsuccessful suggestion is not marked successful. |
| MAIL-009 | VServer session without role | Run mail suggestion with a session whose role is zero. | Service creates a valid administrator session or reports controlled session error. |

## 10. Tenant, database, and master-data helpers

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| DATA-001 | Accounting schema name | Create a schema for a client and currency. | Name is `<Client Name> <Currency Name>`. |
| DATA-002 | Check existing table | Query helper for an existing database table. | Returns positive result on PostgreSQL and Oracle. |
| DATA-003 | Check existing view | Query helper for an existing view. | Returns positive result on both supported database types. |
| DATA-004 | Check missing object | Query helper for nonexistent table/view. | Returns zero. |
| DATA-005 | Screen name mapping exists | Configure active zoom-screen mapping and request old screen name. | Standard screen name is returned. |
| DATA-006 | Screen name mapping absent/inactive | Query with missing mapping or inactive mapping. | Empty string is returned. |
| DATA-007 | Period date with time component | Look up period with date/time near midnight. | Correct period is found based on date portion. |
| DATA-008 | Resource data missing/zero | Update resource product with parent UOM/category/tax values unset or zero. | Existing product values are not overwritten with zero. |
| DATA-009 | Resource data populated | Update resource product with valid parent values. | UOM/category/tax values are synchronized. |
| DATA-010 | COA import exception | Import invalid COA data. | Exception is logged at severe level and useful error is returned. |

## 11. Dashboard widgets and UI

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| UI-001 | Widget data totals | Compare each widget metric with the corresponding approved ERP report/query. | Displayed values match underlying data and currency. |
| UI-002 | Widget empty state | Use a tenant/date range with no matching rows. | Clear no-data state appears. |
| UI-003 | Widget loading state | Delay the widget data response. | Busy overlay displays and disappears after completion. |
| UI-004 | Widget refresh/paging | Refresh and page through widgets with multiple result pages. | Rows/counts update correctly; pager controls behave correctly. |
| UI-005 | Drill-down details | Open detail dialog and compare rows to source data. | Details, references, totals, and page counts match. |
| UI-006 | Allocated/unallocated tabs | Switch allocation detail tabs. | Counts and columns reflect selected tab. |
| UI-007 | Invoice duplicate review | Inspect matching/differing fields and line items. | Match/difference indicators, totals, and line items are correct. |
| UI-008 | Date filters | Apply quick and custom date ranges. | Results update correctly; reversed ranges are rejected. |
| UI-009 | Customer invoice search | Search by partial customer/invoice text and date range. | Matching results appear and keyboard navigation works. |
| UI-010 | Responsive layout | Test desktop, tablet, and phone widths. | No overlap; footer/pager remains usable; intended columns hide. |
| UI-011 | RTL layout | Repeat checks in RTL locale. | Alignment, numeric values, currency, arrows, pagination, and dialogs remain readable. |
| UI-012 | Keyboard accessibility | Navigate widgets/dialogs without mouse. | Focus, actions, close controls, tabs, and pagination are accessible. |
| UI-013 | Dialog close/body scroll | Open and close drill-down dialog. | Dialog closes and body scrolling is restored. |
| UI-014 | Existing dashboard regression | Open dashboards containing older widgets. | Shared styles do not cause unintended appearance or behavior changes. |

## 12. Performance and release regression

| ID | Scenario | Steps | Expected result |
|---|---|---|---|
| PERF-001 | Multi-line allocation baseline | Complete representative allocation with many lines. | Financial results are correct and repeated record loads are reduced. |
| PERF-002 | Multi-line invoice baseline | Complete representative multi-line invoice. | Tax, totals, and costs match baseline with no duration regression. |
| PERF-003 | Multi-line shipment baseline | Complete representative large shipment/receipt documents. | Inventory/costing remain correct and performance does not materially regress. |
| PERF-004 | Concurrent product costing | Run parallel workloads for same and distinct products. | No deadlocks, duplicate IDs, lost updates, or incorrect cost balances. |
| PERF-005 | Cache freshness | Change client costing settings and test within/after cache lifetime. | Existing cache is stable; expired cache observes updated settings. |
| PERF-006 | Long-running workload | Repeat document processing in test environment. | No unbounded cache/timing-memory growth. |
| PERF-007 | Two database engines | Run relevant tests on PostgreSQL and Oracle where supported. | SQL syntax and behavior are correct on both. |
| PERF-008 | Version verification | Build and inspect assembly metadata. | `ModelLibrary` file version is `2.0.1.0`. |

## Suggested test environment

- Use a non-production tenant and disposable test documents.
- Include FIFO and LIFO products, attribute set instances, multiple warehouses, and accounting schemas.
- Include paid/unpaid invoice schedules, cash/payment allocations, and invoices with/without orders.
- Test optional modules both installed and absent as applicable.
- Include users with different access, organization settings, and RTL locale.
- Record before/after amounts and statuses; verify no partial writes on failure.

## Test result tracking template

| Test ID | Environment / build | Tester | Actual result / evidence | Status | Defect link |
|---|---|---|---|---|---|
|  |  |  |  | Not Run |  |
