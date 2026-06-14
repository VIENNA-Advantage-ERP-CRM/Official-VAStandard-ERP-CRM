/**
 * GL Journal Entries KPI Widget
 * Purpose  : Display the count of actual GL Journal documents (PostingType='A')
 *            in the current calendar month. Clicking opens a drill-down dialog
 *            with the journal vouchers for the same month.
 * Table    : GL_Journal, GL_JournalLine
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
        'DR': 'VAS-glje-pill-draft',
        'CO': 'VAS-glje-pill-posted',
        'CL': 'VAS-glje-pill-posted',
        'IP': 'VAS-glje-pill-submit',
        'AP': 'VAS-glje-pill-posted',
        'NA': 'VAS-glje-pill-pending',
        'VO': 'VAS-glje-pill-voided',
        'RE': 'VAS-glje-pill-returned'
    };

    VAS.VAS_041_GLJournalEntriesWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div class="VAS-glje-root">');
        var $kpiValue;
        var $whyText;
        var $titleEl;
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
            var $bsy = $('<div id="VAS-glje-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-glje-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function showDialogBusy(show) {
            if (!$dialogBusy || !$dialogBusy[0]) { return; }
            $dialogBusy[0].style.visibility = show ? 'visible' : 'hidden';
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

            var html = '<div class="kpi kpi-blue" role="button" tabindex="0">'
                + '<div class="w-head">'
                +   '<div class="w-icon">' + svgIcon + '</div>'
                +   '<div class="w-title" id="VAS-glje-title-' + id + '">'
                +     lbl('VAS_041_GLJEntries', 'Entries') + ' &middot; &mdash;'
                +   '</div>'
                +   '<span class="VAS-glje-zoom" aria-hidden="true">'
                +     '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round">'
                +       '<path d="M9 18l6-6-6-6"></path>'
                +     '</svg>'
                +   '</span>'
                + '</div>'
                + '<div class="kpi-value" id="VAS-glje-val-' + id + '">&mdash;</div>'
                + '<div class="kpi-why">'
                +   '<span class="kpi-why-text" id="VAS-glje-why-' + id + '">&mdash;</span>'
                + '</div>'
                + '</div>';

            $root.append(html);

            $kpiValue = $root.find('#VAS-glje-val-' + id);
            $whyText = $root.find('#VAS-glje-why-' + id);
            $titleEl = $root.find('#VAS-glje-title-' + id);

            $root.find('.kpi').on('click', function () { openDialog(); });
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
                url: baseUrl + 'VAS/VAS_041_GLJournalEntriesWidget/GetMonthlyEntryCount',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data) {
                            $titleEl.html(lbl('VAS_041_GLJEntries', 'Entries') + ' &middot; ' + (data.MonthAbbr || ''));
                            $kpiValue.text(typeof data.EntryCount === 'number' ? data.EntryCount : 0);
                            $whyText.text(lbl('VAS_041_AllJournalEntries', 'All journal entries posted in') + ' ' + (data.MonthName || '') + '.');
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
            var title = lbl('VAS_041_GLJEntries', 'Entries') + ' - ' + lbl('VAS_041_ThisMonth', 'This Month');

            $dialog = $('<div class="VAS-glje-dialog" id="VAS-glje-dialog-' + id + '" style="display:none" role="dialog" aria-modal="true">'
                + '<div class="VAS-glje-dialog-scrim"></div>'
                + '<div class="VAS-glje-dialog-card">'
                +   '<div class="VAS-glje-dialog-head">'
                +     '<div class="VAS-glje-dialog-icon">' + svgIcon + '</div>'
                +     '<div class="VAS-glje-dialog-title-wrap">'
                +       '<div class="VAS-glje-dialog-title">' + esc(title) + '</div>'
                +       '<div class="VAS-glje-dialog-sub" id="VAS-glje-dialog-sub-' + id + '">&mdash;</div>'
                +     '</div>'
                +     '<button type="button" class="VAS-glje-dialog-close" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">'
                +       '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                +       '<line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>'
                +     '</button>'
                +   '</div>'
                +   '<div class="VAS-glje-dialog-body">'
                +     '<div class="VAS-glje-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>'
                +     '<div class="VAS-glje-table-wrap" id="VAS-glje-dialog-body-' + id + '"></div>'
                +   '</div>'
                +   '<div class="VAS-glje-dialog-footer">'
                +     '<span class="VAS-glje-dialog-total" id="VAS-glje-dialog-total-' + id + '"></span>'
                +     '<div class="VAS-glje-dialog-actions">'
                +       '<button type="button" class="VAS-glje-export">' + esc(lbl('VAS_Export', 'Export')) + '</button>'
                +       '<button type="button" class="VAS-glje-close-primary">' + esc(lbl('VAS_Close', 'Close')) + '</button>'
                +     '</div>'
                +   '</div>'
                + '</div>'
                + '</div>');

            $dialogBody = $dialog.find('#VAS-glje-dialog-body-' + id);
            $dialogFooterText = $dialog.find('#VAS-glje-dialog-total-' + id);
            $dialogBusy = $dialog.find('.VAS-glje-dialog-busy');
            $dialogBusy[0].style.visibility = 'hidden';

            $dialog.find('.VAS-glje-dialog-close, .VAS-glje-close-primary, .VAS-glje-dialog-scrim').on('click', function () {
                closeDialog();
            });
            $dialog.find('.VAS-glje-export').on('click', function () { exportDialogRows(); });
            $(document).on('keydown.VAS-glje-' + id, function (e) {
                if (e.key === 'Escape' && $dialog && $dialog.is(':visible')) { closeDialog(); }
            });

            $('body').append($dialog);
        }

        function createDetailDialog(svgIcon) {
            var id = $self.AD_UserHomeWidgetID;

            $detailDialog = $('<div class="VAS-glje-dialog VAS-glje-detail-dialog" id="VAS-glje-detail-dialog-' + id + '" style="display:none" role="dialog" aria-modal="true">'
                + '<div class="VAS-glje-dialog-scrim"></div>'
                + '<div class="VAS-glje-dialog-card VAS-glje-detail-card">'
                +   '<div class="VAS-glje-dialog-head">'
                +     '<div class="VAS-glje-dialog-icon">' + svgIcon + '</div>'
                +     '<div class="VAS-glje-dialog-title-wrap">'
                +       '<div class="VAS-glje-dialog-title" id="VAS-glje-detail-title-' + id + '">&mdash;</div>'
                +       '<div class="VAS-glje-dialog-sub" id="VAS-glje-detail-sub-' + id + '">&mdash;</div>'
                +     '</div>'
                +     '<button type="button" class="VAS-glje-dialog-close" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">'
                +       '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                +       '<line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>'
                +     '</button>'
                +   '</div>'
                +   '<div class="VAS-glje-dialog-body VAS-glje-detail-body">'
                +     '<div class="VAS-glje-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>'
                +     '<div class="VAS-glje-detail-content" id="VAS-glje-detail-body-' + id + '"></div>'
                +   '</div>'
                +   '<div class="VAS-glje-dialog-footer">'
                +     '<button type="button" class="VAS-glje-export VAS-glje-detail-close">' + esc(lbl('VAS_Close', 'Close')) + '</button>'
                +     '<div class="VAS-glje-dialog-actions">'
                +       '<button type="button" class="VAS-glje-export">' + esc(lbl('VAS_041_Approve', 'Approve')) + '</button>'
                +       '<button type="button" class="VAS-glje-close-primary">' + esc(lbl('VAS_041_PostJournal', 'Post journal')) + '</button>'
                +     '</div>'
                +   '</div>'
                + '</div>'
                + '</div>');

            $detailBody = $detailDialog.find('#VAS-glje-detail-body-' + id);
            $detailBusy = $detailDialog.find('.VAS-glje-dialog-busy');
            $detailBusy[0].style.visibility = 'hidden';

            $detailDialog.find('.VAS-glje-dialog-close, .VAS-glje-detail-close, .VAS-glje-dialog-scrim').on('click', function () {
                closeDetailDialog();
            });

            $('body').append($detailDialog);
        }

        function openDialog() {
            if (!$dialog) { return; }
            $dialog.show();
            $('body').addClass('VAS-glje-body-lock');
            if (!dialogLoaded) { loadDialogRows(); }
        }

        function closeDialog() {
            if (!$dialog) { return; }
            closeDetailDialog();
            $dialog.hide();
            $('body').removeClass('VAS-glje-body-lock');
        }

        function showDetailBusy(show) {
            if (!$detailBusy || !$detailBusy[0]) { return; }
            $detailBusy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function openDetailDialog(journalId) {
            if (!$detailDialog || !journalId) { return; }
            $detailDialog.show();
            loadJournalDetail(journalId);
        }

        function closeDetailDialog() {
            if (!$detailDialog) { return; }
            $detailDialog.hide();
        }

        function loadDialogRows() {
            showDialogBusy(true);
            $.ajax({
                url: baseUrl + 'VAS/VAS_041_GLJournalEntriesWidget/GetMonthlyEntries',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (result) {
                    try {
                        var data = JSON.parse(result);
                        renderDialog(data || {});
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
            var monthName = data.MonthName || '';
            var year = data.Year || '';

            $dialog.find('#VAS-glje-dialog-sub-' + $self.AD_UserHomeWidgetID).text(
                'All GL journal vouchers created in ' + monthName + ' ' + year
            );

            if (!rows.length) {
                $dialogBody.html('<div class="VAS-glje-dialog-empty">' + esc(lbl('VIS_NoData', 'No data available.')) + '</div>');
                $dialogFooterText.text('');
                return;
            }

            var html = '<table class="VAS-glje-dialog-table">'
                + '<thead><tr>'
                + '<th>' + esc(lbl('VAS_041_JournalNo', 'Journal No.')) + '</th>'
                + '<th>' + esc(lbl('VAS_044_Date', 'Date')) + '</th>'
                + '<th>' + esc(lbl('VAS_044_Description', 'Description')) + '</th>'
                + '<th>' + esc(lbl('VAS_044_Status', 'Status')) + '</th>'
                + '<th class="VAS-glje-num">' + esc(lbl('VAS_041_TotalDebit', 'Total Debit')) + '</th>'
                + '<th class="VAS-glje-num">' + esc(lbl('VAS_041_TotalCredit', 'Total Credit')) + '</th>'
                + '</tr></thead><tbody>';

            for (var i = 0; i < rows.length; i++) {
                var row = rows[i];
                var pillCls = PILL_CLASS[row.DocStatus] || 'VAS-glje-pill-draft';
                var debit = symbol + formatAmount(row.TotalDebit, precision);
                var credit = symbol + formatAmount(row.TotalCredit, precision);
                html += '<tr class="VAS-glje-entry-row" data-journal-id="' + row.GL_Journal_ID + '" title="' + esc(row.DocumentNo + ' - ' + row.StatusName + ', ' + debit) + '">'
                    + '<td class="VAS-glje-doc">' + esc(row.DocumentNo) + '</td>'
                    + '<td class="VAS-glje-date">' + esc(row.DateAcct) + '</td>'
                    + '<td class="VAS-glje-desc">' + esc(row.Description) + '</td>'
                    + '<td><span class="VAS-glje-pill ' + pillCls + '"><span></span>' + esc(row.StatusName) + '</span></td>'
                    + '<td class="VAS-glje-amt">' + esc(debit) + '</td>'
                    + '<td class="VAS-glje-amt">' + esc(credit) + '</td>'
                    + '</tr>';
            }
            html += '</tbody></table>';

            $dialogBody.html(html);
            $dialogBody.find('.VAS-glje-entry-row').on('click', function () {
                openDetailDialog($(this).data('journal-id'));
            });
            $dialogFooterText.text(
                rows.length + ' journals · total ' + symbol + formatAmount(data.TotalDebit, precision)
            );
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
                        if (data && !data.error) {
                            renderJournalDetail(data);
                        } else {
                            renderDetailError();
                        }
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
            var pillCls = PILL_CLASS[journal.DocStatus] || 'VAS-glje-pill-draft';
            var id = $self.AD_UserHomeWidgetID;

            $detailDialog.find('#VAS-glje-detail-title-' + id).text(
                (journal.DocumentNo || '') + ' · ' + (journal.Description || '')
            );
            $detailDialog.find('#VAS-glje-detail-sub-' + id).text(
                (journal.StatusName || '') + ' · ' + (journal.DateAcct || '')
            );

            var totalDebit = symbol + formatAmount(journal.TotalDebit, precision);
            var totalCredit = symbol + formatAmount(journal.TotalCredit, precision);
            var book = (journal.AccountingBook || 'Primary') + (data.ISOCode ? ' · ' + data.ISOCode : '');

            var html = '<div class="VAS-glje-detail-summary">'
                + '<div><span>Journal No.</span><strong>' + esc(journal.DocumentNo) + '</strong></div>'
                + '<div><span>Date</span><strong>' + esc(journal.DateAcct) + '</strong></div>'
                + '<div><span>Status</span><strong><span class="VAS-glje-pill ' + pillCls + '"><span></span>' + esc(journal.StatusName) + '</span></strong></div>'
                + '<div><span>Accounting Book</span><strong>' + esc(book) + '</strong></div>'
                + '<div><span>Total Debit</span><strong>' + esc(totalDebit) + '</strong></div>'
                + '<div><span>Total Credit</span><strong>' + esc(totalCredit) + '</strong></div>'
                + '<div class="VAS-glje-detail-description"><span>Description</span><strong>' + esc(journal.Description) + '</strong></div>'
                + '</div>'
                + '<div class="VAS-glje-detail-section-title">Journal Lines</div>'
                + '<div class="VAS-glje-detail-lines-wrap">'
                + '<table class="VAS-glje-detail-lines">'
                + '<thead><tr>'
                + '<th>Account</th><th>Debit</th><th>Credit</th><th>Cost Center</th><th>Business Partner</th><th>Product</th><th>Project</th>'
                + '</tr></thead><tbody>';

            for (var i = 0; i < lines.length; i++) {
                var line = lines[i];
                html += '<tr>'
                    + '<td>' + esc((line.AccountCode || '') + ' · ' + (line.AccountName || '')) + '</td>'
                    + '<td class="VAS-glje-amt">' + esc(line.Debit > 0 ? symbol + formatAmount(line.Debit, precision) : '-') + '</td>'
                    + '<td class="VAS-glje-amt">' + esc(line.Credit > 0 ? symbol + formatAmount(line.Credit, precision) : '-') + '</td>'
                    + '<td>' + esc(line.CostCenter || '-') + '</td>'
                    + '<td>' + esc(line.BPartner || '-') + '</td>'
                    + '<td>' + esc(line.Product || '-') + '</td>'
                    + '<td>' + esc(line.Project || '-') + '</td>'
                    + '</tr>';
            }

            html += '</tbody><tfoot><tr>'
                + '<td>Total</td>'
                + '<td class="VAS-glje-amt">' + esc(totalDebit) + '</td>'
                + '<td class="VAS-glje-amt">' + esc(totalCredit) + '</td>'
                + '<td colspan="4"></td>'
                + '</tr></tfoot></table></div>'
                + '<div class="VAS-glje-created-strip">'
                + '<span class="VAS-glje-avatar">' + esc(initials(journal.CreatedByName)) + '</span>'
                + '<div><span>Created By</span><strong>' + esc(journal.CreatedByName || '-') + '</strong>'
                + (journal.CreatedDate ? ' · drafted ' + esc(journal.CreatedDate) : '')
                + '</div></div>';

            $detailBody.html(html);
        }

        function renderDetailError() {
            $detailBody.html('<div class="VAS-glje-dialog-empty">' + esc(lbl('VIS_Error', 'Error loading data.')) + '</div>');
        }

        function initials(name) {
            var parts = String(name || '').trim().split(/\s+/);
            if (!parts.length || !parts[0]) { return '--'; }
            if (parts.length === 1) { return parts[0].charAt(0).toUpperCase(); }
            return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
        }

        function renderDialogError() {
            $dialogBody.html('<div class="VAS-glje-dialog-empty">' + esc(lbl('VIS_Error', 'Error loading data.')) + '</div>');
            $dialogFooterText.text('');
        }

        function exportDialogRows() {
            var $table = $dialogBody.find('.VAS-glje-dialog-table');
            if (!$table.length) { return; }

            var excelHtml = '<html xmlns:o="urn:schemas-microsoft-com:office:office"'
                + ' xmlns:x="urn:schemas-microsoft-com:office:excel"'
                + ' xmlns="http://www.w3.org/TR/REC-html40">'
                + '<head><meta charset="utf-8"></head><body>'
                + $table[0].outerHTML
                + '</body></html>';

            var blob = new Blob([excelHtml], { type: 'application/vnd.ms-excel;charset=utf-8;' });
            var url = URL.createObjectURL(blob);
            var a = document.createElement('a');
            a.href = url;
            a.download = 'gl-journal-entries.xls';
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
            URL.revokeObjectURL(url);
        }

        this.refreshWidget = function () {
            dialogLoaded = false;
            $kpiValue.html('&mdash;');
            $whyText.html('&mdash;');
            loadData();
        };

        this.getRoot = function () { return $root; };

        this.disposeComponent = function () {
            $(document).off('keydown.VAS-glje-' + $self.AD_UserHomeWidgetID);
            $('body').removeClass('VAS-glje-body-lock');
            if ($detailDialog) { $detailDialog.remove(); $detailDialog = null; }
            if ($dialog) { $dialog.remove(); $dialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_041_GLJournalEntriesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_041_GLJournalEntriesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_041_GLJournalEntriesWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_041_GLJournalEntriesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
