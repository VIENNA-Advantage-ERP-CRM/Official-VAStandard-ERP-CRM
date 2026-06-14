/**
 * GL Journal Unposted KPI Widget
 * Purpose  : Display unposted GL journals and open drill-down dialogs.
 * Table    : GL_Journal
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    function esc(str) {
        return String(str || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function formatAmount(amount, precision) {
        var stdPrecision = VIS.Env.getCtx().getStdPrecision();
        var prec = (typeof precision === 'number' && precision >= 0) ? precision : stdPrecision;
        return parseFloat(amount || 0).toLocaleString(window.navigator.language, {
            minimumFractionDigits: prec,
            maximumFractionDigits: prec
        });
    }

    var PILL_CLASS = {
        'DR': 'VAS-glju-pill-draft',
        'CO': 'VAS-glju-pill-posted',
        'CL': 'VAS-glju-pill-posted',
        'IP': 'VAS-glju-pill-submit',
        'AP': 'VAS-glju-pill-posted',
        'NA': 'VAS-glju-pill-pending',
        'RE': 'VAS-glju-pill-returned'
    };

    VAS.VAS_036_GLJournalUnpostedWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="VAS-glju-root">');
        var $kpiValue;
        var $whyText;
        var $dialog;
        var $dialogBody;
        var $dialogFooterText;
        var $dialogBusy;
        var $detailDialog;
        var $detailBody;
        var $detailBusy;
        var dialogLoaded = false;
        var baseUrl = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            showBusy(true);
            loadData();
            setInterval(function () { $self.refreshWidget(); }, 1000 * 60 * 5);
        };

        function createBusyIndicator() {
            var $bsy = $('<div id="VAS-glju-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-glju-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function docIconSvg() {
            return '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"></path>'
                + '<polyline points="14 2 14 8 20 8"></polyline>'
                + '<path d="M16 13H8M16 17H8"></path>'
                + '</svg>';
        }

        function createWidget() {
            var svgIcon = docIconSvg();
            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="kpi kpi-amber" role="button" tabindex="0">'
                + '<div class="w-head">'
                + '<div class="w-icon">' + svgIcon + '</div>'
                + '<div class="w-title">' + lbl('VAS_036_GLJUnposted', 'Unposted') + '</div>'
                + '</div>'
                + '<div class="kpi-value warning" id="VAS-glju-val-' + id + '">&mdash;</div>'
                + '<div class="kpi-why">'
                + '<span class="kpi-why-text" id="VAS-glju-why-' + id + '">'
                + lbl('VAS_036_DraftsWaiting', 'Drafts waiting to be approved + posted.')
                + '</span>'
                + '</div>'
                + '</div>';

            $root.append(html);
            $kpiValue = $root.find('#VAS-glju-val-' + id);
            $whyText = $root.find('#VAS-glju-why-' + id);

            $root.find('.kpi').on('click', openDialog);
            $root.find('.kpi').on('keydown', function (e) {
                if (e.key === 'Enter' || e.key === ' ') {
                    e.preventDefault();
                    openDialog();
                }
            });

            createDialog(svgIcon);
            createDetailDialog(svgIcon);
        }

        function loadData() {
            showBusy(true);
            $.ajax({
                url: baseUrl + 'VAS/VAS_041_GLJournalEntriesWidget/GetUnpostedCount',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data) {
                            $kpiValue.text(typeof data.UnpostedCount === 'number' ? data.UnpostedCount : 0);
                        } else {
                            $kpiValue.text(0);
                            $whyText.text(lbl('VIS_NoData', 'No data available.'));
                        }
                    } catch (e) {
                        $kpiValue.html('&mdash;');
                        $whyText.text(lbl('VIS_Error', 'Error loading data.'));
                    }
                    showBusy(false);
                },
                error: function () {
                    $kpiValue.html('&mdash;');
                    $whyText.text(lbl('VIS_Error', 'Error loading data.'));
                    showBusy(false);
                }
            });
        }

        function createDialog(svgIcon) {
            var id = $self.AD_UserHomeWidgetID;
            $dialog = $('<div class="VAS-glju-dialog" id="VAS-glju-dialog-' + id + '" style="display:none" role="dialog" aria-modal="true">'
                + '<div class="VAS-glju-dialog-scrim"></div>'
                + '<div class="VAS-glju-dialog-card">'
                + '<div class="VAS-glju-dialog-head">'
                + '<div class="VAS-glju-dialog-icon">' + svgIcon + '</div>'
                + '<div class="VAS-glju-dialog-title-wrap">'
                + '<div class="VAS-glju-dialog-title">' + esc(lbl('VAS_036_UnpostedJournals', 'Unposted Journals')) + '</div>'
                + '<div class="VAS-glju-dialog-sub">' + esc(lbl('VAS_036_UnpostedSub', 'Drafts, submitted and pending approval - not yet posted to GL')) + '</div>'
                + '</div>'
                + '<button type="button" class="VAS-glju-dialog-close" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">'
                + '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>'
                + '</button></div>'
                + '<div class="VAS-glju-dialog-body">'
                + '<div class="VAS-glju-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>'
                + '<div class="VAS-glju-table-wrap" id="VAS-glju-dialog-body-' + id + '"></div>'
                + '</div>'
                + '<div class="VAS-glju-dialog-footer">'
                + '<span class="VAS-glju-dialog-total" id="VAS-glju-dialog-total-' + id + '"></span>'
                + '<div class="VAS-glju-dialog-actions">'
                + '<button type="button" class="VAS-glju-export">' + esc(lbl('VAS_Export', 'Export')) + '</button>'
                + '<button type="button" class="VAS-glju-close-primary">' + esc(lbl('VAS_Close', 'Close')) + '</button>'
                + '</div></div></div></div>');

            $dialogBody = $dialog.find('#VAS-glju-dialog-body-' + id);
            $dialogFooterText = $dialog.find('#VAS-glju-dialog-total-' + id);
            $dialogBusy = $dialog.find('.VAS-glju-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';
            $dialog.find('.VAS-glju-dialog-close, .VAS-glju-close-primary, .VAS-glju-dialog-scrim').on('click', closeDialog);
            $dialog.find('.VAS-glju-export').on('click', exportDialogRows);
            $(document).on('keydown.VAS-glju-' + id, function (e) {
                if (e.key === 'Escape') {
                    if ($detailDialog && $detailDialog.is(':visible')) { closeDetailDialog(); }
                    else if ($dialog && $dialog.is(':visible')) { closeDialog(); }
                }
            });
            $('body').append($dialog);
        }

        function createDetailDialog(svgIcon) {
            var id = $self.AD_UserHomeWidgetID;
            $detailDialog = $('<div class="VAS-glju-dialog VAS-glju-detail-dialog" id="VAS-glju-detail-dialog-' + id + '" style="display:none" role="dialog" aria-modal="true">'
                + '<div class="VAS-glju-dialog-scrim"></div>'
                + '<div class="VAS-glju-dialog-card VAS-glju-detail-card">'
                + '<div class="VAS-glju-dialog-head">'
                + '<div class="VAS-glju-dialog-icon">' + svgIcon + '</div>'
                + '<div class="VAS-glju-dialog-title-wrap">'
                + '<div class="VAS-glju-dialog-title" id="VAS-glju-detail-title-' + id + '">&mdash;</div>'
                + '<div class="VAS-glju-dialog-sub" id="VAS-glju-detail-sub-' + id + '">&mdash;</div>'
                + '</div>'
                + '<button type="button" class="VAS-glju-dialog-close" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">'
                + '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>'
                + '</button></div>'
                + '<div class="VAS-glju-dialog-body VAS-glju-detail-body">'
                + '<div class="VAS-glju-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>'
                + '<div class="VAS-glju-detail-content" id="VAS-glju-detail-body-' + id + '"></div>'
                + '</div>'
                + '<div class="VAS-glju-dialog-footer">'
                + '<div class="VAS-glju-dialog-actions">'
                + '<button type="button" class="VAS-glju-export">' + esc(lbl('VAS_041_Approve', 'Approve')) + '</button>'
                + '<button type="button" class="VAS-glju-close-primary">' + esc(lbl('VAS_041_PostJournal', 'Post journal')) + '</button>'
                + '</div></div></div></div>');

            $detailBody = $detailDialog.find('#VAS-glju-detail-body-' + id);
            $detailBusy = $detailDialog.find('.VAS-glju-dialog-busy');
            $detailBusy[0].style.visibility = 'hidden';
            $detailDialog.find('.VAS-glju-dialog-close, .VAS-glju-dialog-scrim').on('click', closeDetailDialog);
            $('body').append($detailDialog);
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) { return; }
            $dialogBusy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function showDetailBusy(show) {
            if (!$detailBusy || !$detailBusy[0]) { return; }
            $detailBusy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function openDialog() {
            if (!$dialog) { return; }
            $dialog.show();
            $('body').addClass('VAS-glju-body-lock');
            if (!dialogLoaded) { loadDialogRows(); }
        }

        function closeDialog() {
            closeDetailDialog();
            if ($dialog) { $dialog.hide(); }
            $('body').removeClass('VAS-glju-body-lock');
        }

        function openDetailDialog(journalId) {
            if (!$detailDialog || !journalId) { return; }
            $detailDialog.show();
            loadJournalDetail(journalId);
        }

        function closeDetailDialog() {
            if ($detailDialog) { $detailDialog.hide(); }
        }

        function loadDialogRows() {
            showDialogBusy(true);
            $.ajax({
                url: baseUrl + 'VAS/VAS_041_GLJournalEntriesWidget/GetUnpostedEntries',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (result) {
                    try {
                        renderDialog(JSON.parse(result) || {});
                        dialogLoaded = true;
                    } catch (e) {
                        renderDialogError();
                    }
                    showDialogBusy(false);
                },
                error: function () {
                    renderDialogError();
                    showDialogBusy(false);
                }
            });
        }

        function renderDialog(data) {
            var rows = data.Entries || [];
            var symbol = data.CurSymbol || data.ISOCode || '';
            var precision = data.StdPrecision;

            if (!rows.length) {
                $dialogBody.html('<div class="VAS-glju-dialog-empty">' + esc(lbl('VIS_NoData', 'No data available.')) + '</div>');
                $dialogFooterText.text('');
                return;
            }

            var html = '<table class="VAS-glju-dialog-table"><thead><tr>'
                + '<th>' + esc(lbl('VAS_041_JournalNo', 'Journal No.')) + '</th>'
                + '<th>' + esc(lbl('VAS_044_Date', 'Date')) + '</th>'
                + '<th>' + esc(lbl('VAS_044_Description', 'Description')) + '</th>'
                + '<th>' + esc(lbl('VAS_044_Status', 'Status')) + '</th>'
                + '<th>' + esc(lbl('VAS_041_TotalDebit', 'Total Debit')) + '</th>'
                + '<th>' + esc(lbl('VAS_041_TotalCredit', 'Total Credit')) + '</th>'
                + '</tr></thead><tbody>';

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var pillCls = PILL_CLASS[row.DocStatus] || 'VAS-glju-pill-draft';
                var debit = symbol + formatAmount(row.TotalDebit, precision);
                var credit = symbol + formatAmount(row.TotalCredit, precision);
                html += '<tr class="VAS-glju-entry-row" data-journal-id="' + row.GL_Journal_ID + '">'
                    + '<td class="VAS-glju-doc">' + esc(row.DocumentNo) + '</td>'
                    + '<td class="VAS-glju-date">' + esc(row.DateAcct) + '</td>'
                    + '<td class="VAS-glju-desc">' + esc(row.Description) + '</td>'
                    + '<td><span class="VAS-glju-pill ' + pillCls + '"><span></span>' + esc(row.StatusName) + '</span></td>'
                    + '<td class="VAS-glju-amt">' + esc(debit) + '</td>'
                    + '<td class="VAS-glju-amt">' + esc(credit) + '</td>'
                    + '</tr>';
            }
            html += '</tbody></table>';
            $dialogBody.html(html);
            $dialogBody.find('.VAS-glju-entry-row').on('click', function () {
                openDetailDialog($(this).data('journal-id'));
            });
            $dialogFooterText.text(rows.length + ' journals - total ' + symbol + formatAmount(data.TotalDebit, precision));
        }

        function loadJournalDetail(journalId) {
            showDetailBusy(true);
            $detailBody.html('');
            $.ajax({
                url: baseUrl + 'VAS/VAS_041_GLJournalEntriesWidget/GetJournalEntryDetail',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: { journalId: journalId },
                success: function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data && !data.error) { renderJournalDetail(data); }
                        else { renderDetailError(); }
                    } catch (e) {
                        renderDetailError();
                    }
                    showDetailBusy(false);
                },
                error: function () {
                    renderDetailError();
                    showDetailBusy(false);
                }
            });
        }

        function renderJournalDetail(data) {
            var journal = data.Journal || {};
            var lines = data.Lines || [];
            var symbol = data.CurSymbol || data.ISOCode || '';
            var precision = data.StdPrecision;
            var pillCls = PILL_CLASS[journal.DocStatus] || 'VAS-glju-pill-draft';
            var id = $self.AD_UserHomeWidgetID;
            var totalDebit = symbol + formatAmount(journal.TotalDebit, precision);
            var totalCredit = symbol + formatAmount(journal.TotalCredit, precision);
            var book = (journal.AccountingBook || 'Primary') + (data.ISOCode ? ' - ' + data.ISOCode : '');

            $detailDialog.find('#VAS-glju-detail-title-' + id).text((journal.DocumentNo || '') + ' - ' + (journal.Description || ''));
            $detailDialog.find('#VAS-glju-detail-sub-' + id).text((journal.StatusName || '') + ' - ' + (journal.DateAcct || ''));

            var html = '<div class="VAS-glju-detail-summary">'
                + '<div><span>Journal No.</span><strong>' + esc(journal.DocumentNo) + '</strong></div>'
                + '<div><span>Date</span><strong>' + esc(journal.DateAcct) + '</strong></div>'
                + '<div><span>Status</span><strong><span class="VAS-glju-pill ' + pillCls + '"><span></span>' + esc(journal.StatusName) + '</span></strong></div>'
                + '<div><span>Accounting Book</span><strong>' + esc(book) + '</strong></div>'
                + '<div><span>Total Debit</span><strong>' + esc(totalDebit) + '</strong></div>'
                + '<div><span>Total Credit</span><strong>' + esc(totalCredit) + '</strong></div>'
                + '<div class="VAS-glju-detail-description"><span>Description</span><strong>' + esc(journal.Description) + '</strong></div>'
                + '</div><div class="VAS-glju-detail-section-title">Journal Lines</div>'
                + '<div class="VAS-glju-detail-lines-wrap"><table class="VAS-glju-detail-lines"><thead><tr>'
                + '<th>Account</th><th>Debit</th><th>Credit</th><th>Cost Center</th><th>Business Partner</th><th>Product</th><th>Project</th>'
                + '</tr></thead><tbody>';

            for (var i = 0; i < lines.length; i++) {
                var line = lines[i];
                html += '<tr><td>' + esc((line.AccountCode || '') + ' - ' + (line.AccountName || '')) + '</td>'
                    + '<td class="VAS-glju-amt">' + esc(line.Debit > 0 ? symbol + formatAmount(line.Debit, precision) : '-') + '</td>'
                    + '<td class="VAS-glju-amt">' + esc(line.Credit > 0 ? symbol + formatAmount(line.Credit, precision) : '-') + '</td>'
                    + '<td>' + esc(line.CostCenter || '-') + '</td><td>' + esc(line.BPartner || '-') + '</td>'
                    + '<td>' + esc(line.Product || '-') + '</td><td>' + esc(line.Project || '-') + '</td></tr>';
            }

            html += '</tbody><tfoot><tr><td>Total</td><td class="VAS-glju-amt">' + esc(totalDebit) + '</td>'
                + '<td class="VAS-glju-amt">' + esc(totalCredit) + '</td><td colspan="4"></td></tr></tfoot></table></div>'
                + '<div class="VAS-glju-created-strip"><span class="VAS-glju-avatar">' + esc(initials(journal.CreatedByName)) + '</span>'
                + '<div><span>Created By</span><strong>' + esc(journal.CreatedByName || '-') + '</strong>'
                + (journal.CreatedDate ? ' - drafted ' + esc(journal.CreatedDate) : '') + '</div></div>';

            $detailBody.html(html);
        }

        function renderDialogError() {
            $dialogBody.html('<div class="VAS-glju-dialog-empty">' + esc(lbl('VIS_Error', 'Error loading data.')) + '</div>');
            $dialogFooterText.text('');
        }

        function renderDetailError() {
            $detailBody.html('<div class="VAS-glju-dialog-empty">' + esc(lbl('VIS_Error', 'Error loading data.')) + '</div>');
        }

        function initials(name) {
            var parts = String(name || '').trim().split(/\s+/);
            if (!parts.length || !parts[0]) { return '--'; }
            if (parts.length === 1) { return parts[0].charAt(0).toUpperCase(); }
            return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
        }

        function exportDialogRows() {
            var $table = $dialogBody.find('.VAS-glju-dialog-table');
            if (!$table.length) { return; }
            var excelHtml = '<html xmlns:o="urn:schemas-microsoft-com:office:office" xmlns:x="urn:schemas-microsoft-com:office:excel" xmlns="http://www.w3.org/TR/REC-html40">'
                + '<head><meta charset="utf-8"></head><body>' + $table[0].outerHTML + '</body></html>';
            var blob = new Blob([excelHtml], { type: 'application/vnd.ms-excel;charset=utf-8;' });
            var url = URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = 'unposted-journals.xls';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        }

        this.refreshWidget = function () {
            dialogLoaded = false;
            $kpiValue.html('&mdash;');
            loadData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.VAS-glju-' + $self.AD_UserHomeWidgetID);
            $('body').removeClass('VAS-glju-body-lock');
            if ($detailDialog) { $detailDialog.remove(); $detailDialog = null; }
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_036_GLJournalUnpostedWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_036_GLJournalUnpostedWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_036_GLJournalUnpostedWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_036_GLJournalUnpostedWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
