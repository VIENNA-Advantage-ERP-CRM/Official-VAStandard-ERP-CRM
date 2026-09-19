# Onfinity ERP and CRM

**Open source ERP and CRM in C#/.NET, formerly VIENNA Advantage.** Accounting, purchasing, sales and CRM, inventory and warehouse, projects, fixed assets and HR on one database, with a low-code Application Dictionary underneath: windows, fields, rules, workflows and reports are metadata, so a vertical built on Onfinity survives every upgrade. Runs on Windows Server (IIS) with PostgreSQL or Oracle; users work in the browser.

Onfinity was named VIENNA Advantage until 2024. The company, the product and this code are the same; reviews, articles and directory listings under the old name describe this system.

[![Download on SourceForge](https://img.shields.io/sourceforge/dm/erp-crm-advant?label=SourceForge%20downloads%2Fmonth)](https://sourceforge.net/projects/erp-crm-advant/) [![Licence: EPL](https://img.shields.io/badge/licence-Eclipse%20Public%20Licence-blue)](#licence)

| | |
|---|---|
| Website | https://onfinity.io |
| The free edition, what is in it and what the licence allows | https://onfinity.io/open-source-erp.php |
| Ready-to-run packages (PostgreSQL and Oracle) | https://sourceforge.net/projects/erp-crm-advant/files/ |
| Docker deployment | https://github.com/VIENNA-Advantage-ERP-CRM/OnfinityContainer |
| Brochures, one per module, PDF | https://onfinity.io/brochures.php |
| Community portal: manuals, videos, training, tickets | https://login.onfinity.io/register.aspx (free registration) |
| Release notes | https://viennaadvantage.atlassian.net/wiki/spaces/VA/pages/1769505/Release+Notes |

## What is in it

| Area | What it does |
|---|---|
| Accounting | General ledger with a chart of accounts and journals; receivables, payables, bank and cash with statement matching; budgets with commitment control; cost centres, projects and products as dimensions on the same entries; several companies each with its own books, calendar and currency; parallel accounting schemas (two calendars, accrual and cash); realised and unrealised currency gains; tax per line by category and jurisdiction; withholding on bills and on payments at separate rates. |
| Purchasing | Requisition, request for quotation with several responses, purchase order, goods receipt against the order with the difference recorded, quality plan per item at the gate, supplier bill matched to order and receipt, payment; landed cost from real freight and duty bills spread six ways; vendor price lists and rating; minimum and maximum replenishment. |
| Sales and CRM | Leads with a score and stages, opportunities, quotations from the opportunity, price lists per customer and currency with dated versions, quantity discounts; sales orders with stock, made-to-order, service and contract lines; deliveries, invoices, credit limit with watch and hold, dunning levels, commissions; service contracts with a billing schedule; the customer's history on one record. |
| Inventory and warehouse | Warehouses and bins; on hand, reserved, on order and available to promise; lots and serials with expiry; movements confirmed on both sides; picking in waves, packages; valuation by standard, average, FIFO and other methods; physical count. |
| Projects | Phases and tasks, project types as templates, project lines as the budget, budget against actual with commitments, resources with availability, time and expense, milestone billing, work in progress to asset. |
| Fixed assets | Register by class, location and custodian; straight line and written down value with a rate or life per class; impairment and enhancement; insurance; transfer and disposal with the gain or loss. |
| HR | Employee record, contracts, recruitment, attendance and shifts, leave, appraisals, training, self-service. |
| Platform | The Application Dictionary (tables, windows, tabs, fields, validation rules, callouts, processes, print formats, menus, roles, all as metadata); document workflows with approval limits; the change log on every record (old value, new value, user, time); translations per language; dashboards; a REST API; the mobile app; the Onfinity Market for modules and updates. |

Bills of material are part of the core, and manufacturing execution (work centres, routings, work orders, shop-floor time, work order costing, multi-level MRP) is free in Pluto, the community edition. Payroll with country deductions, country tax localisations such as India GST and e-Invoicing, the document management system and the AI assistant are delivered as modules through the Onfinity Market, some free and some commercial. The [editions page](https://onfinity.io/erp-editions-comparison.php) says which is which.

## Repositories

Onfinity is built from three repositories, in this order:

| Repository | Contents |
|---|---|
| [Official-VABaseFiles](https://github.com/VIENNA-Advantage-ERP-CRM/Official-VABaseFiles) | Base and core libraries, the generated model classes (`XModel`), the print engine. |
| [Official-VAFramework](https://github.com/VIENNA-Advantage-ERP-CRM/Official-VAFramework) | The framework: the Application Dictionary model and engine (`VAModelAD`), workflows (`VAWorkflow`), the HTML5 client and its server-side logic (`VIS`, `VISLogic`). |
| **Official-VAStandard-ERP-CRM** (this repository) | The ERP and CRM application: the business model classes (`ModelLibrary`), the application areas (`VAS`) and their logic (`VASLogic`), the web project (`ViennaAdvantageWeb`). |

Add-on modules live in their own repositories (for example [VA009 Payment Management](https://github.com/VIENNA-Advantage-ERP-CRM/VA009_PaymentManagement), [VA012 Bank Statement](https://github.com/VIENNA-Advantage-ERP-CRM/VA012_BankStatement), [VA027 Post Dated Cheque](https://github.com/VIENNA-Advantage-ERP-CRM/VA027_PostDatedCheque)). [official-VAFramework-ERP-CRM-4.X](https://github.com/VIENNA-Advantage-ERP-CRM/official-VAFramework-ERP-CRM-4.X) is the earlier single-solution repository of the 4.x line, kept for reference. A Docker deployment of the released packages is in [OnfinityContainer](https://github.com/VIENNA-Advantage-ERP-CRM/OnfinityContainer).

## Technology

- C# on the .NET Framework, ASP.NET on IIS (Windows Server)
- HTML5 client in the browser; a mobile app
- PostgreSQL 12 or later, or Oracle 12c or later
- Images for AWS and Oracle Cloud; a Docker deployment (Windows container for the application, Linux container for PostgreSQL)

## Running it

The quickest route is the released package: download the PostgreSQL or Oracle package from [SourceForge](https://sourceforge.net/projects/erp-crm-advant/files/), follow the [installation guide](https://onfinity.io/downloads/va-html5-version/installation-instructions/VIENNA-Advantage-Community-Edition-Installation-and-Basic-Setting.pdf), or use the [Docker deployment](https://github.com/VIENNA-Advantage-ERP-CRM/OnfinityContainer).

To build from source, see [BUILD.md](BUILD.md).

## Support and community

Registration in the [community portal](https://login.onfinity.io/register.aspx) is free and opens the user manuals, business process flows, video tutorials, online training and the ticketing system. Modules and updates are delivered through the Onfinity Market inside the system.

Bug reports and pull requests are welcome here on GitHub. Every contribution is reviewed before it goes into a release.

## Licence

Eclipse Public Licence. Extensions, industry verticals and localisations you write on top are yours; the licence does not oblige you to publish their source. See https://onfinity.io/open-source-erp.php for what the licence allows.
