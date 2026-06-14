/**
 * GL Journal Top Ledger Movement Widget
 * Purpose  : Horizontal bar chart of the top 10 accounts by absolute net
 *            movement (DR − CR) for posted GL journals, in the accounting
 *            schema base currency. Supports month / YTD period toggle.
 * Tables   : GL_Journal, GL_JournalLine, C_ElementValue, C_AcctSchema, C_Currency
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_043_TopLedgerMovement, VAS_043_ByValue, VAS_043_Month, VAS_043_YTD
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

    // ──────────────────────────────────────────────────────────────────────────
    VAS.VAS_043_GLJournalTopMovementWidget = function () {

        this.frame;
        this.windowNo;
        var $self        = this;
        var $root        = $('<div class="VAS-gljtm-root">');
        var activePeriod = 'month';
        var pageNo       = 1;
        var baseUrl      = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            showBusy(true);
            loadData();
            setInterval(function () { $self.refreshWidget(); }, 1000 * 60 * 5);
        };

        function createBusyIndicator() {
            var $bsy = $('<div id="VAS-gljtm-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljtm-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            var listIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<line x1="3" y1="12" x2="21" y2="12"></line>'
                + '<line x1="3" y1="6" x2="21" y2="6"></line>'
                + '<line x1="3" y1="18" x2="21" y2="18"></line>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="VAS-gljtm-card">'

                + '<div class="w-head">'
                +   '<div class="VAS-gljtm-icon">' + listIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_043_TopLedgerMovement', 'Top Ledger Movement') + '</div>'
                +   '<span class="VAS-gljtm-sub">' + lbl('VAS_043_ByValue', 'by value') + '</span>'
                +   '<button class="VAS-gljtm-toggle" id="VAS-gljtm-toggle-' + id + '">'
                +     lbl('VAS_043_Month', 'Month')
                +   '</button>'
                + '</div>'

                + '<div class="VAS-gljtm-body" id="VAS-gljtm-body-' + id + '"></div>'

                + '</div>';

            $root.append(html);

            $root.find('#VAS-gljtm-toggle-' + id).on('click', function () {
                activePeriod = (activePeriod === 'month') ? 'ytd' : 'month';
                pageNo = 1;
                $(this).text(activePeriod === 'month'
                    ? lbl('VAS_043_Month', 'Month')
                    : lbl('VAS_043_YTD', 'YTD'));
                showBusy(true);
                loadData();
            });
        }

        function loadData() {
            $.ajax({
                url: baseUrl + 'VAS/VAS_043_GLJournalTopMovementWidget/GetTopMovement',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                data     : { period: activePeriod, pageNo: pageNo },
                success  : function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data && data.Accounts) {
                            renderBars(data);
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

        function renderBars(data) {
            var id       = $self.AD_UserHomeWidgetID;
            var accounts = data.Accounts || [];
            var sym      = esc(data.currencySymbol || data.CurSymbol || data.currencyISO || data.ISOCode || '');
            var prec     = data.stdPrecision || data.StdPrecision;
            var $body    = $root.find('#VAS-gljtm-body-' + id);

            if (accounts.length === 0) {
                $body.html('<div class="VAS-gljtm-empty">' + lbl('VIS_NoData', 'No data available.') + '</div>');
                return;
            }

            var html = '<div class="VAS-gljtm-list">';
            for (var i = 0; i < accounts.length; i++) {
                var a       = accounts[i];
                var fillCls = 'VAS-gljtm-fill' + (a.IsCredit ? ' VAS-gljtm-fill-cr' : '');
                var valStr  = sym + fmtAmt(a.NetMovement, prec);
                var rowInfo = esc((a.AccountCode || '') + ' ' + (a.AccountName || '')
                    + ' - Net Movement: ' + valStr
                    + ', Debit: ' + sym + fmtAmt(a.TotalDebit, prec)
                    + ', Credit: ' + sym + fmtAmt(a.TotalCredit, prec));

                html += '<div class="VAS-gljtm-row" title="' + rowInfo + '">'
                    +     '<div class="VAS-gljtm-row-head">'
                    +       '<span class="VAS-gljtm-label">'
                    +         '<span class="VAS-gljtm-acct">' + esc(a.AccountCode) + '</span>'
                    +         esc(a.AccountName)
                    +       '</span>'
                    +       '<span class="VAS-gljtm-val">' + esc(valStr) + '</span>'
                    +     '</div>'
                    +     '<div class="VAS-gljtm-track" title="' + rowInfo + '">'
                    +       '<div class="' + fillCls + '" style="--bar-w:' + a.BarPct + '%"></div>'
                    +     '</div>'
                    +   '</div>';
            }
            html += '</div>';
            html += renderPager(data);
            $body.html(html);
            $body.find('.VAS-gljtm-page-prev').on('click', function () {
                if (pageNo <= 1) { return; }
                pageNo -= 1;
                showBusy(true);
                loadData();
            });
            $body.find('.VAS-gljtm-page-next').on('click', function () {
                var totalPages = parseInt(data.TotalPages || data.totalPages || 0, 10);
                if (!totalPages || pageNo >= totalPages) { return; }
                pageNo += 1;
                showBusy(true);
                loadData();
            });
        }

        function renderPager(data) {
            var totalPages = parseInt(data.TotalPages || data.totalPages || 0, 10);
            var totalCount = parseInt(data.TotalCount || data.totalCount || 0, 10);
            if (totalPages <= 1 && totalCount <= 5) { return ''; }

            var pageText = pageNo + ' / ' + (totalPages || 1);
            var helper = Math.min(totalCount, 10) + ' ' + lbl('VAS_043_TopRecords', 'top records');

            return '<div class="VAS-gljtm-pager">'
                + '<span class="VAS-gljtm-page-helper">' + esc(helper) + '</span>'
                + '<div class="VAS-gljtm-page-actions">'
                + '<button type="button" class="VAS-gljtm-page-btn VAS-gljtm-page-prev"'
                + (pageNo <= 1 ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Previous', 'Previous')) + '">'
                + '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M15 18l-6-6 6-6"></path></svg>'
                + '</button>'
                + '<span class="VAS-gljtm-page-text">' + esc(pageText) + '</span>'
                + '<button type="button" class="VAS-gljtm-page-btn VAS-gljtm-page-next"'
                + (totalPages && pageNo >= totalPages ? ' disabled' : '') + ' aria-label="' + esc(lbl('VAS_Next', 'Next')) + '">'
                + '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round"><path d="M9 18l6-6-6-6"></path></svg>'
                + '</button>'
                + '</div>'
                + '</div>';
        }

        function showError() {
            var id = $self.AD_UserHomeWidgetID;
            $root.find('#VAS-gljtm-body-' + id).html(
                '<div class="VAS-gljtm-empty">' + lbl('VIS_Error', 'Error loading data.') + '</div>'
            );
        }

        this.refreshWidget    = function () { showBusy(true); loadData(); };
        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () { $root.remove(); };
    };

    VAS.VAS_043_GLJournalTopMovementWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_043_GLJournalTopMovementWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_043_GLJournalTopMovementWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_043_GLJournalTopMovementWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
