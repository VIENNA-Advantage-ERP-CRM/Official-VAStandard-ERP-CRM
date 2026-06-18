/**
 * GL Journal Recent Entries Widget
 * Purpose  : Display the 6 most recent GL_Journal documents with document
 *            number, account date, description, status and total debit / credit.
 *            Clicking a row zooms to that document in the GL Journal window.
 * Tables   : GL_Journal, GL_JournalLine, AD_Ref_List, C_AcctSchema, C_Currency
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_044_RecentJournalEntries, VAS_044_Hash, VAS_044_Date,
    //            VAS_044_Description, VAS_044_Status, VAS_044_Debit, VAS_044_Credit
    // ───────────────────────────────────────────────────────────────────────────

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    // Escape HTML to prevent XSS when building innerHTML from server data
    function esc(str) {
        return String(str || '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // Format an amount using schema precision — never hardcoded, locale-aware separators.
    function fmtAmt(amount, precision) {
        var stdPrecision = VIS.Env.getCtx().getStdPrecision();
        var prec  = (typeof precision === 'number' && precision >= 0) ? precision : stdPrecision;
        return parseFloat(amount || 0).toLocaleString(window.navigator.language, {
            minimumFractionDigits: prec,
            maximumFractionDigits: prec
        });
    }

    // Map DocStatus code → CSS pill modifier class
    var PILL_CLASS = {
        'DR': 'VAS-gljr-pill-draft',
        'CO': 'VAS-gljr-pill-posted',
        'IP': 'VAS-gljr-pill-submit',
        'AP': 'VAS-gljr-pill-posted',
        'NA': 'VAS-gljr-pill-pending',
        'VO': 'VAS-gljr-pill-voided',
        'RE': 'VAS-gljr-pill-reverse',
        'CL': 'VAS-gljr-pill-closed'
    };

    // ──────────────────────────────────────────────────────────────────────────
    VAS.VAS_044_GLJournalRecentWidget = function () {

        this.frame;
        this.windowNo;
        var $self    = this;
        var $root    = $('<div class="VAS-gljr-root">');
        var $detailDialog;
        var $detailBody;
        var $detailBusy;
        var currentData;
        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;
        var baseUrl  = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            showBusy(true);
            loadData();
            setInterval(function () { $self.refreshWidget(); }, 1000 * 60 * 5);
        };

        function createBusyIndicator() {
            var $bsy = $('<div id="VAS-gljr-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljr-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            // Document / file SVG icon
            var docIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z"></path>'
                + '<polyline points="14 2 14 8 20 8"></polyline>'
                + '</svg>';

            // Flag / alert SVG icon
            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="VAS-gljr-card">'

                // ── Header ──────────────────────────────────────────────────
                + '<div class="w-head">'
                +   '<div class="VAS-gljr-icon">' + docIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_044_RecentJournalEntries', 'Recent Journal Entries') + '</div>'
                +   '<div class="VAS-gljr-pager">'
                +     '<button type="button" class="VAS-gljr-page-btn VAS-gljr-prev" aria-label="' + lbl('VIS_Previous', 'Previous') + '">&#8249;</button>'
                +     '<span class="VAS-gljr-page-text"></span>'
                +     '<button type="button" class="VAS-gljr-page-btn VAS-gljr-next" aria-label="' + lbl('VIS_Next', 'Next') + '">&#8250;</button>'
                +   '</div>'
                + '</div>'

                // ── Alert strip (hidden until an unbalanced entry is found) ──
                // ── Table ────────────────────────────────────────────────────
                + '<div class="VAS-gljr-table-wrap">'
                +   '<table class="VAS-gljr-table">'
                +     '<thead>'
                +       '<tr>'
                +         '<th>' + lbl('VAS_044_Hash', '#') + '</th>'
                +         '<th>' + lbl('VAS_044_Date', 'Date') + '</th>'
                +         '<th>' + lbl('VAS_044_Description', 'Description') + '</th>'
                +         '<th>' + lbl('VAS_044_Status', 'Status') + '</th>'
                +         '<th class="VAS-gljr-num">' + lbl('VAS_044_Debit', 'Debit') + '</th>'
                +         '<th class="VAS-gljr-num">' + lbl('VAS_044_Credit', 'Credit') + '</th>'
                +       '</tr>'
                +     '</thead>'
                +     '<tbody id="VAS-gljr-tbody-' + id + '">'
                +       '<tr><td colspan="6" class="VAS-gljr-empty">—</td></tr>'
                +     '</tbody>'
                +   '</table>'
                + '</div>'

                + '</div>'; // .VAS-gljr-card

            $root.append(html);

            $root.on('click', '.VAS-gljr-row', function () {
                openDetailDialog($(this).data('id'));
            });

            $root.on('click', '.VAS-gljr-prev', function () {
                if (pageNo <= 1) { return; }
                pageNo--;
                renderRows(currentData || {}, id);
            });

            $root.on('click', '.VAS-gljr-next', function () {
                if (totalPages <= 1 || pageNo >= totalPages) { return; }
                pageNo++;
                renderRows(currentData || {}, id);
            });

            createDetailDialog(docIcon);
        }

        function loadData() {
            $.ajax({
                url      : baseUrl + 'VAS/VAS_044_GLJournalRecentWidget/GetRecentEntries',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                success  : function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data) {
                            var id = $self.AD_UserHomeWidgetID;

                            currentData = data;
                            pageNo = 1;
                            renderRows(data, id);
                        } else {
                            showEmpty();
                        }
                    } catch (e) {
                        showEmpty();
                    }
                    showBusy(false);
                },
                error: function () {
                    showEmpty();
                    showBusy(false);
                }
            });
        }

        function renderRows(data, id) {
            var entries = data.Entries || [];
            var sym     = data.CurSymbol || data.ISOCode || '';
            var prec    = data.StdPrecision;
            var $tbody  = $root.find('#VAS-gljr-tbody-' + id);

            if (!entries.length) {
                $tbody.html('<tr><td colspan="6" class="VAS-gljr-empty">'
                    + lbl('VIS_NoData', 'No data available.') + '</td></tr>');
                totalPages = 0;
                updatePager();
                return;
            }

            totalPages = Math.ceil(entries.length / pageSize);
            if (pageNo > totalPages) { pageNo = totalPages; }
            if (pageNo < 1) { pageNo = 1; }

            var start = (pageNo - 1) * pageSize;
            var pageEntries = entries.slice(start, start + pageSize);
            var html = '';
            for (var i = 0; i < pageEntries.length; i++) {
                var e       = pageEntries[i];
                var pillCls = PILL_CLASS[e.DocStatus] || 'VAS-gljr-pill-draft';
                var drAmt   = e.TotalDebit  > 0 ? sym + fmtAmt(e.TotalDebit,  prec) : '—';
                var crAmt   = e.TotalCredit > 0 ? sym + fmtAmt(e.TotalCredit, prec) : '—';
                var rowCls  = 'VAS-gljr-row' + (e.IsUnbalanced ? ' VAS-gljr-row-unbal' : '');
                var rowInfo = esc(e.DocumentNo + ' - ' + e.StatusName
                    + ', ' + lbl('VAS_044_Debit', 'Debit') + ': ' + drAmt
                    + ', ' + lbl('VAS_044_Credit', 'Credit') + ': ' + crAmt);

                html += '<tr class="' + rowCls + '" data-id="' + e.GL_Journal_ID + '" title="' + rowInfo + '">'
                     + '<td class="VAS-gljr-col-id">'   + esc(e.DocumentNo)  + '</td>'
                     + '<td class="VAS-gljr-col-date">'  + esc(e.DateAcct)    + '</td>'
                     + '<td class="VAS-gljr-col-desc">'  + esc(e.Description) + '</td>'
                     + '<td><span class="VAS-gljr-pill ' + pillCls + '">'
                     +   esc(e.StatusName) + '</span></td>'
                     + '<td class="VAS-gljr-col-num">'  + drAmt + '</td>'
                     + '<td class="VAS-gljr-col-num">'  + crAmt + '</td>'
                     + '</tr>';
            }
            $tbody.html(html);
            updatePager();
        }

        function updatePager() {
            var $pageText = $root.find('.VAS-gljr-page-text');
            var $prevBtn = $root.find('.VAS-gljr-prev');
            var $nextBtn = $root.find('.VAS-gljr-next');

            if (totalPages > 1) {
                $pageText.text(pageNo + ' ' + lbl('VIS_Of', 'of') + ' ' + totalPages);
            } else {
                $pageText.text('');
            }

            $prevBtn.prop('disabled', pageNo <= 1 || totalPages <= 1);
            $nextBtn.prop('disabled', totalPages <= 1 || pageNo >= totalPages);
        }

        function createDetailDialog(svgIcon) {
            var id = $self.AD_UserHomeWidgetID;

            $detailDialog = $('<div class="VAS-gljr-detail-dialog" id="VAS-gljr-detail-dialog-' + id + '" style="display:none" role="dialog" aria-modal="true">'
                + '<div class="VAS-gljr-detail-scrim"></div>'
                + '<div class="VAS-gljr-detail-card">'
                +   '<div class="VAS-gljr-detail-head">'
                +     '<div class="VAS-gljr-detail-icon">' + svgIcon + '</div>'
                +     '<div class="VAS-gljr-detail-title-wrap">'
                +       '<div class="VAS-gljr-detail-title" id="VAS-gljr-detail-title-' + id + '">&mdash;</div>'
                +       '<div class="VAS-gljr-detail-sub" id="VAS-gljr-detail-sub-' + id + '">&mdash;</div>'
                +     '</div>'
                +     '<button type="button" class="VAS-gljr-detail-close-x" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">'
                +       '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                +       '<line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>'
                +     '</button>'
                +   '</div>'
                +   '<div class="VAS-gljr-detail-body">'
                +     '<div class="VAS-gljr-detail-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>'
                +     '<div class="VAS-gljr-detail-content" id="VAS-gljr-detail-body-' + id + '"></div>'
                +   '</div>'
                +   '<div class="VAS-gljr-detail-footer">'
                +     '<button type="button" class="VAS-gljr-detail-secondary VAS-gljr-download">' + esc(lbl('VAS_DownloadPDF', 'Download PDF')) + '</button>'
                +     '<div class="VAS-gljr-detail-actions">'
                +       '<button type="button" class="VAS-gljr-detail-secondary VAS-gljr-action-approve">' + esc(lbl('VAS_041_Approve', 'Approve')) + '</button>'
                +       '<button type="button" class="VAS-gljr-detail-primary VAS-gljr-action-post">' + esc(lbl('VAS_041_PostJournal', 'Post journal')) + '</button>'
                +     '</div>'
                +   '</div>'
                + '</div>'
                + '</div>');

            $detailBody = $detailDialog.find('#VAS-gljr-detail-body-' + id);
            $detailBusy = $detailDialog.find('.VAS-gljr-detail-busy');
            $detailBusy[0].style.visibility = 'hidden';

            $detailDialog.find('.VAS-gljr-detail-close-x, .VAS-gljr-detail-scrim').on('click', function () {
                closeDetailDialog();
            });

            $(document).on('keydown.VAS-gljr-' + id, function (e) {
                if (e.key === 'Escape' && $detailDialog && $detailDialog.is(':visible')) { closeDetailDialog(); }
            });

            $('body').append($detailDialog);
        }

        function openDetailDialog(journalId) {
            if (!$detailDialog || !journalId) { return; }
            $detailDialog.show();
            $('body').addClass('VAS-gljr-body-lock');
            loadJournalDetail(journalId);
        }

        function closeDetailDialog() {
            if (!$detailDialog) { return; }
            $detailDialog.hide();
            $('body').removeClass('VAS-gljr-body-lock');
        }

        function showDetailBusy(show) {
            if (!$detailBusy || !$detailBusy[0]) { return; }
            $detailBusy[0].style.visibility = show ? 'visible' : 'hidden';
        }

        function loadJournalDetail(journalId) {
            showDetailBusy(true);
            $detailBody.html('');
            $.ajax({
                url: baseUrl + 'VAS/VAS_044_GLJournalRecentWidget/GetJournalEntryDetail',
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

        function isReadOnlyStatus(journal) {
            var code = String(journal.DocStatus || '').toUpperCase();
            var name = String(journal.StatusName || '').toUpperCase();
            var posted = String(journal.Posted || '').toUpperCase();
            return posted === 'Y' || code === 'CO' || code === 'CL' || code === 'RE' || name === 'POSTED' || name === 'REVERSED';
        }

        function renderJournalDetail(data) {
            var journal = data.Journal || {};
            var lines = data.Lines || [];
            var symbol = data.CurSymbol || data.ISOCode || '';
            var precision = data.StdPrecision;
            var pillCls = PILL_CLASS[journal.DocStatus] || 'VAS-gljr-pill-draft';
            var id = $self.AD_UserHomeWidgetID;
            var readOnly = isReadOnlyStatus(journal);

            $detailDialog.find('#VAS-gljr-detail-title-' + id).text(
                (journal.DocumentNo || '') + ' · ' + (journal.Description || '')
            );
            $detailDialog.find('#VAS-gljr-detail-sub-' + id).text(
                (journal.StatusName || '') + ' · ' + (journal.DateAcct || '')
            );
            $detailDialog.find('.VAS-gljr-action-approve, .VAS-gljr-action-post').toggle(!readOnly);

            var totalDebit = symbol + fmtAmt(journal.TotalDebit, precision);
            var totalCredit = symbol + fmtAmt(journal.TotalCredit, precision);
            var book = (journal.AccountingBook || 'Primary') + (data.ISOCode ? ' · ' + data.ISOCode : '');

            var html = '<div class="VAS-gljr-detail-summary">'
                + '<div><span>Journal No.</span><strong>' + esc(journal.DocumentNo) + '</strong></div>'
                + '<div><span>Date</span><strong>' + esc(journal.DateAcct) + '</strong></div>'
                + '<div><span>Status</span><strong><span class="VAS-gljr-pill ' + pillCls + '"><span></span>' + esc(journal.StatusName) + '</span></strong></div>'
                + '<div><span>Accounting Book</span><strong>' + esc(book) + '</strong></div>'
                + '<div><span>Total Debit</span><strong>' + esc(totalDebit) + '</strong></div>'
                + '<div><span>Total Credit</span><strong>' + esc(totalCredit) + '</strong></div>'
                + '<div class="VAS-gljr-detail-description"><span>Description</span><strong>' + esc(journal.Description) + '</strong></div>'
                + '</div>'
                + '<div class="VAS-gljr-detail-section-title">Journal Lines</div>'
                + '<div class="VAS-gljr-detail-lines-wrap">'
                + '<table class="VAS-gljr-detail-lines">'
                + '<thead><tr>'
                + '<th>Account</th><th>Debit</th><th>Credit</th><th>Cost Center</th><th>Business Partner</th><th>Product</th><th>Project</th>'
                + '</tr></thead><tbody>';

            for (var i = 0; i < lines.length; i++) {
                var line = lines[i];
                html += '<tr>'
                    + '<td>' + esc((line.AccountCode || '') + ' · ' + (line.AccountName || '')) + '</td>'
                    + '<td class="VAS-gljr-detail-amt">' + esc(line.Debit > 0 ? symbol + fmtAmt(line.Debit, precision) : '-') + '</td>'
                    + '<td class="VAS-gljr-detail-amt">' + esc(line.Credit > 0 ? symbol + fmtAmt(line.Credit, precision) : '-') + '</td>'
                    + '<td>' + esc(line.CostCenter || '-') + '</td>'
                    + '<td>' + esc(line.BPartner || '-') + '</td>'
                    + '<td>' + esc(line.Product || '-') + '</td>'
                    + '<td>' + esc(line.Project || '-') + '</td>'
                    + '</tr>';
            }

            html += '</tbody><tfoot><tr>'
                + '<td>Total</td>'
                + '<td class="VAS-gljr-detail-amt">' + esc(totalDebit) + '</td>'
                + '<td class="VAS-gljr-detail-amt">' + esc(totalCredit) + '</td>'
                + '<td colspan="4"></td>'
                + '</tr></tfoot></table></div>'
                + '<div class="VAS-gljr-created-strip">'
                + '<span class="VAS-gljr-avatar">' + esc(initials(journal.CreatedByName)) + '</span>'
                + '<div><span>Created By</span><strong>' + esc(journal.CreatedByName || '-') + '</strong>'
                + (journal.CreatedDate ? ' · drafted ' + esc(journal.CreatedDate) : '')
                + '</div></div>';

            $detailBody.html(html);
        }

        function initials(name) {
            var parts = String(name || '').trim().split(/\s+/);
            if (!parts.length || !parts[0]) { return '--'; }
            if (parts.length === 1) { return parts[0].charAt(0).toUpperCase(); }
            return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
        }

        function renderDetailError() {
            $detailBody.html('<div class="VAS-gljr-detail-empty">' + esc(lbl('VIS_Error', 'Error loading data.')) + '</div>');
        }

        function showEmpty() {
            var id = $self.AD_UserHomeWidgetID;
            $root.find('#VAS-gljr-tbody-' + id).html(
                '<tr><td colspan="6" class="VAS-gljr-empty">'
                + lbl('VIS_Error', 'Error loading data.') + '</td></tr>'
            );
            totalPages = 0;
            updatePager();
        }

        this.refreshWidget    = function () { showBusy(true); loadData(); };
        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.VAS-gljr-' + $self.AD_UserHomeWidgetID);
            $('body').removeClass('VAS-gljr-body-lock');
            if ($detailDialog) { $detailDialog.remove(); $detailDialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_044_GLJournalRecentWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_044_GLJournalRecentWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_044_GLJournalRecentWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_044_GLJournalRecentWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
