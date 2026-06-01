/**
 * GL Journal Recent Entries Widget
 * Purpose  : Display the 6 most recent GL_Journal documents with document
 *            number, account date, description, status and total debit / credit.
 *            Clicking a row zooms to that document in the GL Journal window.
 *            An alert strip highlights the first unbalanced entry found.
 * Tables   : GL_Journal, GL_JournalLine, AD_Ref_List, C_AcctSchema, C_Currency
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_RecentJournalEntries, VAS_IsUnbalancedBy,
    //            VAS_MultiCurrencyRounding, VAS_Fix, VAS_Hash, VAS_Date,
    //            VAS_Description, VAS_Status, VAS_Debit, VAS_Credit
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
    VIS.GLJournalRecentWidget = function () {

        this.frame;
        this.windowNo;
        var $self    = this;
        var $root    = $('<div class="VAS-gljr-root">');
        var baseUrl  = VIS.Application.contextUrl;
        var windowId = 0;   // GL Journal AD_Window_ID — set after first load

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
            var flagIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z"></path>'
                + '<line x1="4" y1="22" x2="4" y2="15"></line>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="VAS-gljr-card">'

                // ── Header ──────────────────────────────────────────────────
                + '<div class="w-head">'
                +   '<div class="VAS-gljr-icon">' + docIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_RecentJournalEntries', 'Recent Journal Entries') + '</div>'
                + '</div>'

                // ── Alert strip (hidden until an unbalanced entry is found) ──
                + '<div class="VAS-gljr-alert" id="VAS-gljr-alert-' + id + '" style="display:none">'
                +   '<div class="VAS-gljr-alert-icon">' + flagIcon + '</div>'
                +   '<div class="VAS-gljr-alert-body">'
                +     '<div class="VAS-gljr-alert-title" id="VAS-gljr-alert-title-' + id + '"></div>'
                +     '<div class="VAS-gljr-alert-desc">'
                +       lbl('VAS_MultiCurrencyRounding', 'Possible rounding on multi-currency conversion.')
                +     '</div>'
                +   '</div>'
                +   '<button class="VAS-gljr-btn-fix" id="VAS-gljr-btn-fix-' + id + '">'
                +     lbl('VAS_Fix', 'Fix')
                +   '</button>'
                + '</div>'

                // ── Table ────────────────────────────────────────────────────
                + '<div class="VAS-gljr-table-wrap">'
                +   '<table class="VAS-gljr-table">'
                +     '<thead>'
                +       '<tr>'
                +         '<th>' + lbl('VAS_Hash', '#') + '</th>'
                +         '<th>' + lbl('VAS_Date', 'Date') + '</th>'
                +         '<th>' + lbl('VAS_Description', 'Description') + '</th>'
                +         '<th>' + lbl('VAS_Status', 'Status') + '</th>'
                +         '<th class="VAS-gljr-num">' + lbl('VAS_Debit', 'Debit') + '</th>'
                +         '<th class="VAS-gljr-num">' + lbl('VAS_Credit', 'Credit') + '</th>'
                +       '</tr>'
                +     '</thead>'
                +     '<tbody id="VAS-gljr-tbody-' + id + '">'
                +       '<tr><td colspan="6" class="VAS-gljr-empty">—</td></tr>'
                +     '</tbody>'
                +   '</table>'
                + '</div>'

                + '</div>'; // .VAS-gljr-card

            $root.append(html);

            // Fix button → zoom to the unbalanced journal
            $root.find('#VAS-gljr-btn-fix-' + id).on('click', function () {
                var fixId = $(this).data('journal-id');
                if (windowId > 0 && fixId) {
                    VIS.ADialog.openTabAd(windowId, fixId);
                }
            });

            // Row click → zoom to that journal
            $root.find('#VAS-gljr-tbody-' + id).on('click', 'tr[data-id]', function () {
                var jid = $(this).data('id');
                if (windowId > 0 && jid) {
                    VIS.ADialog.openTabAd(windowId, jid);
                }
            });
        }

        function loadData() {
            $.ajax({
                url      : baseUrl + 'VAS/VAS_GLJournalRecent/GetRecentEntries',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                success  : function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data) {
                            var id = $self.AD_UserHomeWidgetID;
                            windowId = data.WindowID || 0;

                            renderAlert(data, id);
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

        function renderAlert(data, id) {
            var $alert = $root.find('#VAS-gljr-alert-' + id);
            if (data.UnbalancedEntry) {
                var u    = data.UnbalancedEntry;
                var diff = fmtAmt(u.Difference, data.StdPrecision);
                var sym  = data.CurSymbol || data.ISOCode || '';
                $root.find('#VAS-gljr-alert-title-' + id).text(
                    esc(u.DocumentNo) + ' '
                    + lbl('VAS_IsUnbalancedBy', 'is unbalanced by')
                    + ' ' + sym + diff
                );
                $root.find('#VAS-gljr-btn-fix-' + id).data('journal-id', u.GL_Journal_ID);
                $alert.show();
            } else {
                $alert.hide();
            }
        }

        function renderRows(data, id) {
            var entries = data.Entries || [];
            var sym     = data.CurSymbol || data.ISOCode || '';
            var prec    = data.StdPrecision;
            var $tbody  = $root.find('#VAS-gljr-tbody-' + id);

            if (!entries.length) {
                $tbody.html('<tr><td colspan="6" class="VAS-gljr-empty">'
                    + lbl('VIS_NoData', 'No data available.') + '</td></tr>');
                return;
            }

            var html = '';
            for (var i = 0; i < entries.length; i++) {
                var e       = entries[i];
                var pillCls = PILL_CLASS[e.DocStatus] || 'VAS-gljr-pill-draft';
                var drAmt   = e.TotalDebit  > 0 ? sym + fmtAmt(e.TotalDebit,  prec) : '—';
                var crAmt   = e.TotalCredit > 0 ? sym + fmtAmt(e.TotalCredit, prec) : '—';
                var rowCls  = 'VAS-gljr-row' + (e.IsUnbalanced ? ' VAS-gljr-row-unbal' : '');

                html += '<tr class="' + rowCls + '" data-id="' + e.GL_Journal_ID + '">'
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
        }

        function showEmpty() {
            var id = $self.AD_UserHomeWidgetID;
            $root.find('#VAS-gljr-tbody-' + id).html(
                '<tr><td colspan="6" class="VAS-gljr-empty">'
                + lbl('VIS_Error', 'Error loading data.') + '</td></tr>'
            );
        }

        this.refreshWidget    = function () { showBusy(true); loadData(); };
        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () { $root.remove(); };
    };

    VIS.GLJournalRecentWidget.prototype.refreshWidget = function () {};

    VIS.GLJournalRecentWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.GLJournalRecentWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.GLJournalRecentWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VIS, jQuery);
