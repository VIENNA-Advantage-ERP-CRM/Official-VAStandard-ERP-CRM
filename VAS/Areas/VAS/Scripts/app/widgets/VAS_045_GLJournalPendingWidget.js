/**
 * GL Journal Pending Action Queue Widget
 * Purpose  : Displays GL journals awaiting user action (Draft, Approval,
 *            Post, Resubmit). Each row shows urgency marker, document info,
 *            age, and amount.
 * Tables   : GL_Journal, GL_JournalLine, C_AcctSchema, C_Currency, AD_User
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_045_PendingActionQueue, VAS_045_Items, VAS_045_Overdue
    // ───────────────────────────────────────────────────────────────────────────

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

    // Precision is always dynamic from C_Currency.StdPrecision — never hardcoded.
    function fmtAmt(amount, precision) {
        var stdPrecision = VIS.Env.getCtx().getStdPrecision();
        var prec = (typeof precision === 'number' && precision >= 0) ? precision : stdPrecision;
        return parseFloat(amount || 0).toLocaleString(window.navigator.language, {
            minimumFractionDigits: prec,
            maximumFractionDigits: prec
        });
    }

    var PILL_CLASS = {
        'DR': 'VAS-gljpq-pill-draft',
        'CO': 'VAS-gljpq-pill-posted',
        'CL': 'VAS-gljpq-pill-posted',
        'IP': 'VAS-gljpq-pill-submit',
        'AP': 'VAS-gljpq-pill-posted',
        'NA': 'VAS-gljpq-pill-pending',
        'RE': 'VAS-gljpq-pill-returned'
    };

    // ──────────────────────────────────────────────────────────────────────────
    VAS.VAS_045_GLJournalPendingWidget = function () {

        this.frame;
        this.windowNo;
        var $self   = this;
        var $root   = $('<div class="VAS-gljpq-root">');
        var $detailDialog;
        var $detailBody;
        var $detailBusy;
        var currentData;
        var pageNo = 1;
        var pageSize = 3;
        var totalPages = 0;
        var baseUrl = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            showBusy(true);
            loadData();
            setInterval(function () { $self.refreshWidget(); }, 1000 * 60 * 5);
        };

        function createBusyIndicator() {
            var $bsy = $('<div id="VAS-gljpq-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljpq-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            var clockIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<circle cx="12" cy="12" r="10"></circle>'
                + '<polyline points="12 6 12 12 16 14"></polyline>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="VAS-gljpq-card">'

                + '<div class="w-head">'
                +   '<div class="VAS-gljpq-icon">' + clockIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_045_PendingActionQueue', 'Pending Action Queue') + '</div>'
                +   '<span class="VAS-gljpq-count" id="VAS-gljpq-count-' + id + '"></span>'
                +   '<div class="VAS-gljpq-pager">'
                +     '<button type="button" class="VAS-gljpq-page-btn VAS-gljpq-prev" aria-label="' + lbl('VIS_Previous', 'Previous') + '">&#8249;</button>'
                +     '<span class="VAS-gljpq-page-text"></span>'
                +     '<button type="button" class="VAS-gljpq-page-btn VAS-gljpq-next" aria-label="' + lbl('VIS_Next', 'Next') + '">&#8250;</button>'
                +   '</div>'
                + '</div>'

                + '<div class="VAS-gljpq-body" id="VAS-gljpq-body-' + id + '"></div>'

                + '</div>';

            $root.append(html);
            $root.on('click', '.VAS-gljpq-prev', function () {
                if (pageNo <= 1) { return; }
                pageNo--;
                render(currentData || {});
            });

            $root.on('click', '.VAS-gljpq-next', function () {
                if (totalPages <= 1 || pageNo >= totalPages) { return; }
                pageNo++;
                render(currentData || {});
            });

            createDetailDialog(clockIcon);
        }

        function loadData() {
            $.ajax({
                url      : baseUrl + 'VAS/VAS_045_GLJournalPendingWidget/GetPendingQueue',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                success  : function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data && data.Queue) {
                            currentData = data;
                            pageNo = 1;
                            render(data);
                        } else {
                            showError();
                        }
                    } catch (e) {
                        showError();
                    }
                    showBusy(false);
                },
                error: function () {
                    showError();
                    showBusy(false);
                }
            });
        }

        function render(data) {
            var id    = $self.AD_UserHomeWidgetID;
            var $body = $root.find('#VAS-gljpq-body-' + id);
            var queue = data.Queue || [];
            var sym   = esc(data.CurSymbol || data.ISOCode || '');
            var prec  = data.StdPrecision;

            // Update item count in header
            $root.find('#VAS-gljpq-count-' + id).text(
                data.TotalCount + ' ' + lbl('VAS_045_Items', 'items')
            );

            if (queue.length === 0) {
                $body.html('<div class="VAS-gljpq-empty">' + lbl('VIS_NoData', 'No pending journals.') + '</div>');
                totalPages = 0;
                updatePager();
                return;
            }

            totalPages = Math.ceil(queue.length / pageSize);
            if (pageNo > totalPages) { pageNo = totalPages; }
            if (pageNo < 1) { pageNo = 1; }

            var start = (pageNo - 1) * pageSize;
            var pageQueue = queue.slice(start, start + pageSize);
            var html = '<div class="VAS-gljpq-list">';
            for (var i = 0; i < pageQueue.length; i++) {
                var item = pageQueue[i];

                var titleStr = esc(item.DocumentNo);
                if (item.Description) { titleStr += ' · ' + esc(item.Description); }

                var ageLabel = item.IsOverdue
                    ? item.AgeStr + ' ' + lbl('VAS_045_Overdue', 'overdue')
                    : item.AgeStr;

                var metaParts = [esc(item.ActionLabel), esc(ageLabel)];
                if (item.UserName) { metaParts.push(esc(item.UserName)); }
                var amountStr = sym + fmtAmt(item.TotalDebit, prec);
                var itemInfo = esc(item.DocumentNo + ' - ' + item.ActionLabel
                    + ', ' + ageLabel
                    + ', ' + amountStr);

                html += '<div class="VAS-gljpq-item" data-journal-id="' + item.GL_Journal_ID + '" title="' + itemInfo + '">'
                    +     '<div class="VAS-gljpq-mrk VAS-gljpq-mrk-' + item.MarkerType + '"></div>'
                    +     '<div class="VAS-gljpq-body-row">'
                    +       '<div class="VAS-gljpq-title">' + titleStr + '</div>'
                    +       '<div class="VAS-gljpq-meta">' + metaParts.join(' · ') + '</div>'
                    +     '</div>'
                    +     '<span class="VAS-gljpq-amt">' + esc(amountStr) + '</span>'
                    +   '</div>';
            }
            html += '</div>';
            $body.html(html);
            $body.find('.VAS-gljpq-item').on('click', function () {
                openDetailDialog($(this).data('journal-id'));
            });
            updatePager();
        }

        function updatePager() {
            var $pageText = $root.find('.VAS-gljpq-page-text');
            var $prevBtn = $root.find('.VAS-gljpq-prev');
            var $nextBtn = $root.find('.VAS-gljpq-next');

            if (totalPages > 1) {
                $pageText.text(pageNo + ' / ' + totalPages);
            } else {
                $pageText.text('');
            }

            $prevBtn.prop('disabled', pageNo <= 1 || totalPages <= 1);
            $nextBtn.prop('disabled', totalPages <= 1 || pageNo >= totalPages);
        }

        function createDetailDialog(svgIcon) {
            var id = $self.AD_UserHomeWidgetID;

            $detailDialog = $('<div class="VAS-gljpq-dialog" id="VAS-gljpq-dialog-' + id + '" style="display:none" role="dialog" aria-modal="true">'
                + '<div class="VAS-gljpq-dialog-scrim"></div>'
                + '<div class="VAS-gljpq-dialog-card">'
                + '<div class="VAS-gljpq-dialog-head">'
                + '<div class="VAS-gljpq-dialog-icon">' + svgIcon + '</div>'
                + '<div class="VAS-gljpq-dialog-title-wrap">'
                + '<div class="VAS-gljpq-dialog-title" id="VAS-gljpq-dialog-title-' + id + '">&mdash;</div>'
                + '<div class="VAS-gljpq-dialog-sub" id="VAS-gljpq-dialog-sub-' + id + '">&mdash;</div>'
                + '</div>'
                + '<button type="button" class="VAS-gljpq-dialog-close-x" aria-label="' + esc(lbl('VAS_Close', 'Close')) + '">'
                + '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">'
                + '<line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>'
                + '</button></div>'
                + '<div class="VAS-gljpq-dialog-body">'
                + '<div class="VAS-gljpq-dialog-busy"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>'
                + '<div class="VAS-gljpq-detail-content" id="VAS-gljpq-detail-body-' + id + '"></div>'
                + '</div>'
                + '<div class="VAS-gljpq-dialog-footer">'
                + '<button type="button" class="VAS-gljpq-dialog-secondary VAS-gljpq-detail-close">' + esc(lbl('VAS_Close', 'Close')) + '</button>'
                + '<div class="VAS-gljpq-dialog-actions">'
                + '<button type="button" class="VAS-gljpq-dialog-secondary VAS-gljpq-action-approve">' + esc(lbl('VAS_041_Approve', 'Approve')) + '</button>'
                + '<button type="button" class="VAS-gljpq-dialog-primary VAS-gljpq-action-post">' + esc(lbl('VAS_041_PostJournal', 'Post journal')) + '</button>'
                + '</div></div></div></div>');

            $detailBody = $detailDialog.find('#VAS-gljpq-detail-body-' + id);
            $detailBusy = $detailDialog.find('.VAS-gljpq-dialog-busy');
            $detailBusy[0].style.visibility = 'hidden';

            $detailDialog.find('.VAS-gljpq-dialog-close-x, .VAS-gljpq-detail-close, .VAS-gljpq-dialog-scrim').on('click', closeDetailDialog);
            $(document).on('keydown.VAS-gljpq-' + id, function (e) {
                if (e.key === 'Escape' && $detailDialog && $detailDialog.is(':visible')) { closeDetailDialog(); }
            });
            $('body').append($detailDialog);
        }

        function openDetailDialog(journalId) {
            if (!$detailDialog || !journalId) { return; }
            $detailDialog.show();
            $('body').addClass('VAS-gljpq-body-lock');
            loadJournalDetail(journalId);
        }

        function closeDetailDialog() {
            if (!$detailDialog) { return; }
            $detailDialog.hide();
            $('body').removeClass('VAS-gljpq-body-lock');
        }

        function showDetailBusy(show) {
            if (!$detailBusy || !$detailBusy[0]) { return; }
            $detailBusy[0].style.visibility = show ? 'visible' : 'hidden';
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
            var pillCls = PILL_CLASS[journal.DocStatus] || 'VAS-gljpq-pill-draft';
            var id = $self.AD_UserHomeWidgetID;
            var totalDebit = symbol + fmtAmt(journal.TotalDebit, precision);
            var totalCredit = symbol + fmtAmt(journal.TotalCredit, precision);
            var book = (journal.AccountingBook || 'Primary') + (data.ISOCode ? ' · ' + data.ISOCode : '');

            $detailDialog.find('#VAS-gljpq-dialog-title-' + id).text((journal.DocumentNo || '') + ' · ' + (journal.Description || ''));
            $detailDialog.find('#VAS-gljpq-dialog-sub-' + id).text((journal.StatusName || '') + ' · ' + (journal.DateAcct || ''));

            var html = '<div class="VAS-gljpq-detail-summary">'
                + '<div><span>Journal No.</span><strong>' + esc(journal.DocumentNo) + '</strong></div>'
                + '<div><span>Date</span><strong>' + esc(journal.DateAcct) + '</strong></div>'
                + '<div><span>Status</span><strong><span class="VAS-gljpq-pill ' + pillCls + '"><span></span>' + esc(journal.StatusName) + '</span></strong></div>'
                + '<div><span>Accounting Book</span><strong>' + esc(book) + '</strong></div>'
                + '<div><span>Total Debit</span><strong>' + esc(totalDebit) + '</strong></div>'
                + '<div><span>Total Credit</span><strong>' + esc(totalCredit) + '</strong></div>'
                + '<div class="VAS-gljpq-detail-description"><span>Description</span><strong>' + esc(journal.Description) + '</strong></div>'
                + '</div>'
                + '<div class="VAS-gljpq-detail-section-title">Journal Lines</div>'
                + '<div class="VAS-gljpq-detail-lines-wrap"><table class="VAS-gljpq-detail-lines"><thead><tr>'
                + '<th>Account</th><th>Debit</th><th>Credit</th><th>Cost Center</th><th>Business Partner</th><th>Product</th><th>Project</th>'
                + '</tr></thead><tbody>';

            for (var i = 0; i < lines.length; i++) {
                var line = lines[i];
                html += '<tr>'
                    + '<td>' + esc((line.AccountCode || '') + ' · ' + (line.AccountName || '')) + '</td>'
                    + '<td class="VAS-gljpq-detail-amt">' + esc(line.Debit > 0 ? symbol + fmtAmt(line.Debit, precision) : '-') + '</td>'
                    + '<td class="VAS-gljpq-detail-amt">' + esc(line.Credit > 0 ? symbol + fmtAmt(line.Credit, precision) : '-') + '</td>'
                    + '<td>' + esc(line.CostCenter || '-') + '</td>'
                    + '<td>' + esc(line.BPartner || '-') + '</td>'
                    + '<td>' + esc(line.Product || '-') + '</td>'
                    + '<td>' + esc(line.Project || '-') + '</td>'
                    + '</tr>';
            }

            html += '</tbody><tfoot><tr>'
                + '<td>Total</td>'
                + '<td class="VAS-gljpq-detail-amt">' + esc(totalDebit) + '</td>'
                + '<td class="VAS-gljpq-detail-amt">' + esc(totalCredit) + '</td>'
                + '<td colspan="4"></td></tr></tfoot></table></div>'
                + '<div class="VAS-gljpq-created-strip"><span class="VAS-gljpq-avatar">' + esc(initials(journal.CreatedByName)) + '</span>'
                + '<div><span>Created By</span><strong>' + esc(journal.CreatedByName || '-') + '</strong>'
                + (journal.CreatedDate ? ' · drafted ' + esc(journal.CreatedDate) : '') + '</div></div>';

            $detailBody.html(html);
        }

        function renderDetailError() {
            $detailBody.html('<div class="VAS-gljpq-dialog-empty">' + esc(lbl('VIS_Error', 'Error loading data.')) + '</div>');
        }

        function initials(name) {
            var parts = String(name || '').trim().split(/\s+/);
            if (!parts.length || !parts[0]) { return '--'; }
            if (parts.length === 1) { return parts[0].charAt(0).toUpperCase(); }
            return (parts[0].charAt(0) + parts[1].charAt(0)).toUpperCase();
        }

        function showError() {
            var id = $self.AD_UserHomeWidgetID;
            $root.find('#VAS-gljpq-body-' + id).html(
                '<div class="VAS-gljpq-empty">' + lbl('VIS_Error', 'Error loading data.') + '</div>'
            );
            totalPages = 0;
            updatePager();
        }

        this.refreshWidget    = function () { showBusy(true); loadData(); };
        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () {
            $(document).off('keydown.VAS-gljpq-' + $self.AD_UserHomeWidgetID);
            $('body').removeClass('VAS-gljpq-body-lock');
            if ($detailDialog) { $detailDialog.remove(); $detailDialog = null; }
            $root.remove();
        };
    };

    VAS.VAS_045_GLJournalPendingWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_045_GLJournalPendingWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_045_GLJournalPendingWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_045_GLJournalPendingWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
