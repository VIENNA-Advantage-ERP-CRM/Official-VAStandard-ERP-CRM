# Generates VAS_PaymentVoucherPanel_AD_Messages.xlsx with the new VAS_PV* keys
# introduced by the Payment Voucher tab panel.
#
# DEDUPLICATION RULE
# Before adding any row here, the same message text must not already exist
# either in AD_Message or anywhere else in this project. Texts that already
# resolve via AD_Message — including standard platform keys (Created, Date,
# Status, Invoice, Open, Edit, Active, Pending, Description, Amount, Cancel,
# Save, Close, Payment) and prior VAS_* keys (VAS_Draft, VAS_Approved,
# VAS_Closed, VAS_Partial, VAS_Paid, VAS_PaymentDate, VAS_DownloadPDF, etc.)
# are reused directly from JS/C# and intentionally NOT included below.

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSCommandPath

Add-Type -Path (Join-Path $root 'packages\DocumentFormat.OpenXml.2.7.2\lib\net46\DocumentFormat.OpenXml.dll')
Add-Type -Path (Join-Path $root 'packages\System.IO.Packaging.4.0.0\lib\net46\System.IO.Packaging.dll')
Add-Type -Path (Join-Path $root 'packages\ExcelNumberFormat.1.0.10\lib\netstandard2.0\ExcelNumberFormat.dll')
Add-Type -Path (Join-Path $root 'packages\ClosedXML.0.95.4\lib\net46\ClosedXML.dll')

$rows = @(
    # --- Title -----------------------------------------------------------
    @{ V = 'VAS_PaymentVoucherOverview';    T = 'Payment Voucher Overview' },

    # --- Status pill text (only the variants whose text isn't already
    #     registered as VAS_Draft / VAS_Approved) -------------------------
    @{ V = 'VAS_PVStateSubmitted';          T = 'Submitted' },
    @{ V = 'VAS_PVStateReviewed';           T = 'Reviewed' },
    @{ V = 'VAS_PVStateReleased';           T = 'Released' },
    @{ V = 'VAS_PVStatus_Submitted';        T = 'In workflow' },
    @{ V = 'VAS_PVAwaitingRelease';         T = 'Awaiting release' },

    # --- Header icon button labels (Download PDF reuses VAS_DownloadPDF) -
    @{ V = 'VAS_PVEmailAction';             T = 'Email voucher' },
    @{ V = 'VAS_PVMoreAction';              T = 'More actions' },
    @{ V = 'VAS_PVActionStub';              T = 'This action is not yet available' },

    # --- Summary metric cards (Amount/Payment date reuse existing keys) --
    @{ V = 'VAS_PVPayee';                   T = 'Payee' },
    @{ V = 'VAS_PVApproval';                T = 'Approval' },
    @{ V = 'VAS_PVBank';                    T = 'Bank' },
    @{ V = 'VAS_PVYtdPaid';                 T = 'YTD paid · {0}' },
    @{ V = 'VAS_PVApproversOf';             T = '{0} of {1} approvers' },
    @{ V = 'VAS_PVFinalBy';                 T = 'Final by {0}, {1}' },
    @{ V = 'VAS_PVScheduledIn';             T = 'Scheduled · in {0} days' },
    @{ V = 'VAS_PVOverdue';                 T = 'Overdue' },

    # --- Stage tracker (Active/Pending/Approved reuse existing keys) -----
    @{ V = 'VAS_PVStageDrafted';            T = 'Drafted' },

    # --- Release queue ---------------------------------------------------
    @{ V = 'VAS_PVTreasuryQueue';           T = 'Treasury queue' },
    @{ V = 'VAS_PVWaitingDays';             T = 'waiting {0} days' },
    @{ V = 'VAS_PVReleaseNow';              T = 'Release now' },
    @{ V = 'VAS_PVReleasing';               T = 'Releasing…' },
    @{ V = 'VAS_PVReleaseFailed';           T = "Couldn't release. Retry" },
    @{ V = 'VAS_PVReleaseAriaLabel';        T = 'Release payment voucher {0}' },
    @{ V = 'VAS_PVReleaseNotFound';         T = 'Payment voucher not found' },
    @{ V = 'VAS_PVReleaseNotApproved';      T = 'Voucher must be approved before release' },
    @{ V = 'VAS_PVReleaseNotAllowed';       T = 'Voucher type does not support release' },

    # --- Allocated invoices (Amount/Closed/Partial reuse existing keys) --
    @{ V = 'VAS_PVAllocatedInvoices';       T = 'Allocated invoices' },
    @{ V = 'VAS_PVFullyMatched';            T = 'Fully matched' },
    @{ V = 'VAS_PVPartiallyMatched';        T = 'Partially matched' },
    @{ V = 'VAS_PVUnmatched';               T = 'Unmatched' },
    @{ V = 'VAS_PVColAllocated';            T = 'Allocated' },
    @{ V = 'VAS_PVColBalance';              T = 'Balance' },
    @{ V = 'VAS_PVTotalAllocated';          T = 'Total allocated' },
    @{ V = 'VAS_PVNoInvoices';              T = 'No invoices allocated to this voucher.' },

    # --- Payment notes ---------------------------------------------------
    @{ V = 'VAS_PVPaymentNotes';            T = 'Payment notes' },
    @{ V = 'VAS_PVNoNotes';                 T = 'No notes added.' },
    @{ V = 'VAS_PVTerms';                   T = 'Terms' },
    @{ V = 'VAS_PVGLPosting';               T = 'GL posting' },
    @{ V = 'VAS_PVCostCenter';              T = 'Cost center' },
    @{ V = 'VAS_PVReference';               T = 'Reference' },
    @{ V = 'VAS_PVPostedYes';               T = 'Posted' },
    @{ V = 'VAS_PVPostedNo';                T = 'Not posted' },

    # --- Recent activity (Approved/Reviewed/Released/Created reuse keys) -
    @{ V = 'VAS_PVRecentActivity';          T = 'Recent activity' },
    @{ V = 'VAS_PVEvents';                  T = '{0} events' },
    @{ V = 'VAS_PVNoActivity';              T = 'No activity recorded yet.' },
    @{ V = 'VAS_PVBy';                      T = 'by' },
    @{ V = 'VAS_PVActRejected';             T = 'Rejected' },
    @{ V = 'VAS_PVActNoteEdited';           T = 'Notes edited' },
    @{ V = 'VAS_PVActAllocationRevised';    T = 'Allocation revised' },

    # --- States / errors -------------------------------------------------
    @{ V = 'VAS_PVVoucherNotFound';         T = 'Voucher not found' },
    @{ V = 'VAS_PVCouldntLoadSection';      T = "Couldn't load this section." },
    @{ V = 'VAS_PVRetry';                   T = 'Retry' },
    @{ V = 'VAS_PVBackToVouchers';          T = 'Back to vouchers' }
)

$prefix = 'VAS_'
$module = 'VAS'

$wb = New-Object ClosedXML.Excel.XLWorkbook
$ws = $wb.Worksheets.Add('AD_Message')

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

$out = Join-Path $root 'VAS_PaymentVoucherPanel_AD_Messages.xlsx'
$wb.SaveAs($out)

Write-Output ("Wrote {0} rows to: {1}" -f $rows.Count, $out)
