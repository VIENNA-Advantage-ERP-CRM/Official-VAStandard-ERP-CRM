# Generates VAS_InvoiceOverview_AD_Messages.xlsx with Value / MsgText / Prefix / Module columns.
#
# DEDUPLICATION RULE (per VAS/CLAUDE.md "Message Deduplication")
# Before adding any row here, the same MsgText must not already exist in
# AD_Message under a key with no prefix or with VIS_/AD_ prefix. Texts that
# already resolve via existing platform AD_Message rows under bare keys
# (Customer, Notes, Description, Type, Quantity, Amount, Total, Cancel,
# Active, Pending, Created, Date, Status, Edit, Open, Invoice, Payment,
# DueDate, CheckNo, etc.)
# are reused directly from VAS_InvoiceOverview.js and intentionally NOT
# included below.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath

# Load ClosedXML + dependencies (already on disk via NuGet packages).
Add-Type -Path (Join-Path $root 'packages\DocumentFormat.OpenXml.2.7.2\lib\net46\DocumentFormat.OpenXml.dll')
Add-Type -Path (Join-Path $root 'packages\System.IO.Packaging.4.0.0\lib\net46\System.IO.Packaging.dll')
Add-Type -Path (Join-Path $root 'packages\ExcelNumberFormat.1.0.10\lib\netstandard2.0\ExcelNumberFormat.dll')
Add-Type -Path (Join-Path $root 'packages\ClosedXML.0.95.4\lib\net46\ClosedXML.dll')

$rows = @(
    # --- Header / sub line / detail row ----------------------------------
    @{ V = 'VAS_InvoiceOverview';                 T = 'Invoice Overview' },
    @{ V = 'VAS_InvoiceNumber';                   T = 'Invoice Number' },
    @{ V = 'VAS_InvoiceDate';                     T = 'Invoice Date' },
    @{ V = 'VAS_Issued';                          T = 'Issued' },
    @{ V = 'VAS_CustomerOpenInvoices';            T = 'open invoices' },
    @{ V = 'VAS_Outstanding';                     T = 'Outstanding' },
    @{ V = 'VAS_DaysRemaining';                   T = 'days remaining' },
    @{ V = 'VAS_DaysOverdue';                     T = 'days overdue' },
    @{ V = 'VAS_OfTotal';                         T = 'of total' },

    # --- Status timeline -------------------------------------------------
    @{ V = 'VAS_Draft';                           T = 'Draft' },
    @{ V = 'VAS_Approved';                        T = 'Approved' },
    @{ V = 'VAS_Sent';                            T = 'Sent' },
    @{ V = 'VAS_Paid';                            T = 'Paid' },
    @{ V = 'VAS_Closed';                          T = 'Closed' },
    @{ V = 'VAS_Since';                           T = 'Since' },
    @{ V = 'VAS_Due';                             T = 'Due' },

    # --- Action buttons --------------------------------------------------
    @{ V = 'VAS_RecordPayment';                   T = 'Record payment' },
    @{ V = 'VAS_SendReminder';                    T = 'Send reminder' },
    @{ V = 'VAS_DownloadPDF';                     T = 'Download PDF' },
    @{ V = 'VAS_Duplicate';                       T = 'Duplicate' },
    @{ V = 'VAS_ViewLedgerEntry';                 T = 'View ledger entry' },

    # --- Duplicate flow feedback ----------------------------------------
    @{ V = 'VAS_DuplicateSuccess';                T = 'Duplicated invoice {0}' },
    @{ V = 'VAS_DuplicateFailed';                 T = 'Could not duplicate invoice' },
    @{ V = 'VAS_DuplicateNotAllowedVoidedReversed'; T = 'Cannot duplicate a voided or reversed invoice' },

    # --- Eligible to make recurring banner ------------------------------
    @{ V = 'VAS_EligibleToMakeRecurring';         T = 'Eligible to make recurring' },
    @{ V = 'VAS_AllLinesAreServicesOrExpenses';   T = 'All {0} lines are services or expenses' },
    @{ V = 'VAS_NoPhysicalItemsDetected';         T = 'No physical items detected' },
    @{ V = 'VAS_LinesEligibleForRecurring';       T = '{0} of {1} lines eligible for recurring' },
    @{ V = 'VAS_PhysicalItemsDetected';           T = '{0} physical items detected' },
    @{ V = 'VAS_NoLinesEligibleForRecurring';     T = 'No lines eligible for recurring' },
    @{ V = 'VAS_SetUpRecurring';                  T = 'Set up recurring' },

    # --- Line items table -----------------------------------------------
    @{ V = 'VAS_LineItems';                       T = 'Line items' },
    @{ V = 'VAS_Rate';                            T = 'Rate' },
    @{ V = 'VAS_Subtotal';                        T = 'Subtotal' },
    @{ V = 'VAS_Tax';                             T = 'Tax' },

    # --- Payment risk banner --------------------------------------------
    @{ V = 'VAS_CustomerPaysInDaysOnAverage';     T = '{0} pays in {1} days on average' },
    @{ V = 'VAS_NoPaymentHistoryTitle';           T = '{0} has no payment history' },
    @{ V = 'VAS_LikelyToClearBeforeDueDate';      T = 'Likely to clear before due date' },
    @{ V = 'VAS_UsuallyPaysShortlyAfterDueDate';  T = 'Usually pays shortly after due date' },
    @{ V = 'VAS_FrequentlyPaysLate';              T = 'Frequently pays late' },
    @{ V = 'VAS_NoPreviousPaidInvoicesFound';     T = 'No previous paid invoices found' },
    @{ V = 'VAS_LowRisk';                         T = 'Low risk' },
    @{ V = 'VAS_MediumRisk';                      T = 'Medium risk' },
    @{ V = 'VAS_HighRisk';                        T = 'High risk' },
    @{ V = 'VAS_NoHistory';                       T = 'No history' },

    # --- Notes section --------------------------------------------------
    @{ V = 'VAS_AddNote';                         T = '+ Add note' },
    @{ V = 'VAS_NoNotes';                         T = 'No notes' },
    @{ V = 'VAS_NoteCountOne';                    T = 'note' },
    @{ V = 'VAS_NoteCountMany';                   T = 'notes' },
    @{ V = 'VAS_VisibleOnInvoicePDF';             T = 'visible on invoice PDF' },
    @{ V = 'VAS_VisibleToCustomer';               T = 'Visible to customer' },
    @{ V = 'VAS_Internal';                        T = 'Internal' },
    @{ V = 'VAS_Edited';                          T = 'Edited' },

    # --- Generic --------------------------------------------------------
    @{ V = 'VAS_NoData';                          T = 'No data' }
)

$prefix = 'VAS_'
$module = 'VAS'

$wb = New-Object ClosedXML.Excel.XLWorkbook
$ws = $wb.Worksheets.Add('AD_Message')

# Header row
$ws.Cell(1, 1).Value = 'Value'
$ws.Cell(1, 2).Value = 'MsgText'
$ws.Cell(1, 3).Value = 'Prefix'
$ws.Cell(1, 4).Value = 'Module'

$header = $ws.Range('A1:D1')
$header.Style.Font.Bold = $true
$header.Style.Fill.BackgroundColor = [ClosedXML.Excel.XLColor]::LightGray

$r = 2
foreach ($row in $rows) {
    $ws.Cell($r, 1).Value = $row.V
    $ws.Cell($r, 2).Value = $row.T
    $ws.Cell($r, 3).Value = $prefix
    $ws.Cell($r, 4).Value = $module
    $r++
}

$ws.Columns().AdjustToContents() | Out-Null
$ws.SheetView.FreezeRows(1) | Out-Null

$out = Join-Path $root 'VAS_InvoiceOverview_AD_Messages.xlsx'
$wb.SaveAs($out)

Write-Output ("Wrote {0} rows to: {1}" -f $rows.Count, $out)
