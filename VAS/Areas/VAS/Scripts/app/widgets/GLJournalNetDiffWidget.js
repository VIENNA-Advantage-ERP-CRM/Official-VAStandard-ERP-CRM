/**
 * GL Journal Net Difference KPI Widget
 * Purpose  : Display SUM(AmtAcctDr) - SUM(AmtAcctCr) from GL_JournalLine
 *            in the accounting schema base currency for month or YTD.
 *            When the result is zero the books are in balance.
 * Tables   : GL_Journal, GL_JournalLine, C_AcctSchema, C_Currency
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_NetDifference, VAS_DrMinusCr, VAS_Status,
    //            VAS_BooksInBalance, VAS_DebitExceedsCredit, VAS_CreditExceedsDebit,
    //            VAS_YTD
    // ───────────────────────────────────────────────────────────────────────────

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    function formatAmount(amount, precision) {
        var stdPrecision = VIS.Env.getCtx().getStdPrecision();
        var prec  = (typeof precision === 'number' && precision >= 0) ? precision : stdPrecision;
        var fixed = parseFloat(Math.abs(amount)).toFixed(prec);
        var parts = fixed.split('.');
        parts[0]  = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        return { main: parts[0], decimal: parts[1] || '00' };
    }

    // ──────────────────────────────────────────────────────────────────────────
    VIS.GLJournalNetDiffWidget = function () {

        this.frame;
        this.windowNo;
        var $self        = this;
        var $root        = $('<div class="VAS-gljnd-root">');
        var $mainNum;
        var $decimalNum;
        var $curPrefix;
        var $statusText;
        var $valueWrap;
        var activePeriod = 'month';
        var baseUrl      = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            showBusy(true);
            loadData();
            setInterval(function () { $self.refreshWidget(); }, 1000 * 60 * 5);
        };

        function createBusyIndicator() {
            var $bsy = $('<div id="VAS-gljnd-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljnd-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            // Crossing-arrows SVG icon
            var svgIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<path d="M16 3h5v5M4 20L21 3M21 16v5h-5M15 15l6 6M4 4l5 5"></path>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="kpi kpi-teal">'

                + '<div class="w-head">'
                +   '<div class="w-icon">' + svgIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_NetDifference', 'Net Difference') + '</div>'
                +   '<div class="VAS-gljnd-period-toggle" id="VAS-gljnd-toggle-' + id + '">'
                +     '<span class="VAS-gljnd-period VAS-gljnd-period-active" data-period="month"'
                +       ' id="VAS-gljnd-mon-' + id + '">—</span>'
                +     '<span class="VAS-gljnd-sep">&middot;</span>'
                +     '<span class="VAS-gljnd-period" data-period="ytd">'
                +       lbl('VAS_YTD', 'YTD')
                +     '</span>'
                +   '</div>'
                +   '<span class="VAS-gljnd-drcr">' + lbl('VAS_DrMinusCr', 'DR − CR') + '</span>'
                + '</div>'

                + '<div class="kpi-value" id="VAS-gljnd-val-' + id + '">'
                +   '<span class="VAS-gljnd-prefix"  id="VAS-gljnd-pfx-'  + id + '"></span>'
                +   '<span class="VAS-gljnd-main"    id="VAS-gljnd-main-' + id + '">—</span>'
                +   '<span class="VAS-gljnd-decimal" id="VAS-gljnd-dec-'  + id + '"></span>'
                + '</div>'

                + '<div class="kpi-why">'
                +   '<span class="kpi-why-label">' + lbl('VAS_Status', 'Status') + '</span>'
                +   '<span class="kpi-why-text VAS-gljnd-status" id="VAS-gljnd-status-' + id + '">&mdash;</span>'
                + '</div>'

                + '</div>';

            $root.append(html);

            $mainNum    = $root.find('#VAS-gljnd-main-'   + id);
            $decimalNum = $root.find('#VAS-gljnd-dec-'    + id);
            $curPrefix  = $root.find('#VAS-gljnd-pfx-'    + id);
            $statusText = $root.find('#VAS-gljnd-status-' + id);
            $valueWrap  = $root.find('#VAS-gljnd-val-'    + id);

            $root.find('#VAS-gljnd-toggle-' + id).on('click', '.VAS-gljnd-period', function () {
                var $btn = $(this);
                if ($btn.hasClass('VAS-gljnd-period-active')) { return; }
                $root.find('#VAS-gljnd-toggle-' + id)
                     .find('.VAS-gljnd-period')
                     .removeClass('VAS-gljnd-period-active');
                $btn.addClass('VAS-gljnd-period-active');
                activePeriod = $btn.data('period');
                showBusy(true);
                loadData();
            });
        }

        function loadData() {
            $.ajax({
                url      : baseUrl + 'VAS/VAS_GLJournalDebit/GetNetDifference',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                data     : { period: activePeriod },
                success  : function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data) {
                            $root.find('[data-period="month"]').text(data.MonthAbbr || '—');
                            $curPrefix.text(data.CurSymbol || data.ISOCode || '');

                            var fmt = formatAmount(data.NetDiff, data.StdPrecision);
                            $mainNum.text(fmt.main);
                            $decimalNum.text('.' + fmt.decimal);

                            // Colour the value block and status based on balance
                            $valueWrap.removeClass('VAS-gljnd-balanced VAS-gljnd-unbalanced');
                            $statusText.removeClass('VAS-gljnd-status-ok VAS-gljnd-status-warn');

                            if (data.IsBalanced) {
                                $valueWrap.addClass('VAS-gljnd-balanced');
                                $statusText.addClass('VAS-gljnd-status-ok');
                                $statusText.text(lbl('VAS_BooksInBalance',
                                    'Books are in balance · ledger ready for posting.'));
                            } else if (data.NetDiff > 0) {
                                $valueWrap.addClass('VAS-gljnd-unbalanced');
                                $statusText.addClass('VAS-gljnd-status-warn');
                                $statusText.text(lbl('VAS_DebitExceedsCredit', 'Debit exceeds credit.'));
                            } else {
                                $valueWrap.addClass('VAS-gljnd-unbalanced');
                                $statusText.addClass('VAS-gljnd-status-warn');
                                $statusText.text(lbl('VAS_CreditExceedsDebit', 'Credit exceeds debit.'));
                            }
                        } else {
                            $mainNum.text('—');
                            $decimalNum.text('');
                            $statusText.text(lbl('VIS_NoData', 'No data available.'));
                        }
                    } catch (e) {
                        $mainNum.text('—');
                        $decimalNum.text('');
                        $statusText.text(lbl('VIS_Error', 'Error loading data.'));
                    }
                    showBusy(false);
                },
                error: function () {
                    $mainNum.text('—');
                    $decimalNum.text('');
                    $statusText.text(lbl('VIS_Error', 'Error loading data.'));
                    showBusy(false);
                }
            });
        }

        this.refreshWidget    = function () { $mainNum.text('—'); $decimalNum.text(''); loadData(); };
        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () { $root.remove(); };
    };

    VIS.GLJournalNetDiffWidget.prototype.refreshWidget = function () {};

    VIS.GLJournalNetDiffWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.GLJournalNetDiffWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.GLJournalNetDiffWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VIS, jQuery);
