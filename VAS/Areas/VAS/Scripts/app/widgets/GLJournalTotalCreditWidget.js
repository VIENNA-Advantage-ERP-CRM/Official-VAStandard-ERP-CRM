/**
 * GL Journal Total Credit KPI Widget
 * Purpose  : Display the sum of AmtAcctCr from GL_JournalLine
 *            in the accounting schema base currency for month or YTD.
 * Tables   : GL_Journal, GL_JournalLine, C_AcctSchema, C_Currency
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_TotalCredit, VAS_Why, VAS_SumCreditLines,
    //            VAS_JournalEntriesPeriod, VAS_YTD
    // ───────────────────────────────────────────────────────────────────────────

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    function formatAmount(amount, precision) {
        var stdPrecision = VIS.Env.getCtx().getStdPrecision();
        var prec  = (typeof precision === 'number' && precision >= 0) ? precision : stdPrecision;
        var fixed = parseFloat(amount).toFixed(prec);
        var parts = fixed.split('.');
        parts[0]  = parts[0].replace(/\B(?=(\d{3})+(?!\d))/g, ',');
        return { main: parts[0], decimal: parts[1] || '00' };
    }

    // ──────────────────────────────────────────────────────────────────────────
    VIS.GLJournalTotalCreditWidget = function () {

        this.frame;
        this.windowNo;
        var $self        = this;
        var $root        = $('<div class="VAS-gljtc-root">');
        var $mainNum;
        var $decimalNum;
        var $curPrefix;
        var $whyText;
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
            var $bsy = $('<div id="VAS-gljtc-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljtc-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            // Down-arrow SVG icon
            var svgIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<line x1="12" y1="5" x2="12" y2="19"></line>'
                + '<polyline points="5 12 12 19 19 12"></polyline>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="kpi kpi-blue">'

                + '<div class="w-head">'
                +   '<div class="w-icon">' + svgIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_TotalCredit', 'Total Credit') + '</div>'
                +   '<div class="VAS-gljtc-period-toggle" id="VAS-gljtc-toggle-' + id + '">'
                +     '<span class="VAS-gljtc-period VAS-gljtc-period-active" data-period="month"'
                +       ' id="VAS-gljtc-mon-' + id + '">—</span>'
                +     '<span class="VAS-gljtc-sep">&middot;</span>'
                +     '<span class="VAS-gljtc-period" data-period="ytd">'
                +       lbl('VAS_YTD', 'YTD')
                +     '</span>'
                +   '</div>'
                + '</div>'

                + '<div class="kpi-value" id="VAS-gljtc-val-' + id + '">'
                +   '<span class="VAS-gljtc-prefix"  id="VAS-gljtc-pfx-'  + id + '"></span>'
                +   '<span class="VAS-gljtc-main"    id="VAS-gljtc-main-' + id + '">—</span>'
                +   '<span class="VAS-gljtc-decimal" id="VAS-gljtc-dec-'  + id + '"></span>'
                + '</div>'

                + '<div class="kpi-why">'
                +   '<span class="kpi-why-label">' + lbl('VAS_Why', 'Why') + '</span>'
                +   '<span class="kpi-why-text" id="VAS-gljtc-why-' + id + '">&mdash;</span>'
                + '</div>'

                + '</div>';

            $root.append(html);

            $mainNum    = $root.find('#VAS-gljtc-main-' + id);
            $decimalNum = $root.find('#VAS-gljtc-dec-'  + id);
            $curPrefix  = $root.find('#VAS-gljtc-pfx-'  + id);
            $whyText    = $root.find('#VAS-gljtc-why-'  + id);

            $root.find('#VAS-gljtc-toggle-' + id).on('click', '.VAS-gljtc-period', function () {
                var $btn = $(this);
                if ($btn.hasClass('VAS-gljtc-period-active')) { return; }
                $root.find('#VAS-gljtc-toggle-' + id)
                     .find('.VAS-gljtc-period')
                     .removeClass('VAS-gljtc-period-active');
                $btn.addClass('VAS-gljtc-period-active');
                activePeriod = $btn.data('period');
                showBusy(true);
                loadData();
            });
        }

        function loadData() {
            $.ajax({
                url      : baseUrl + 'VAS/VAS_GLJournalDebit/GetTotalCredit',
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
                            var fmt = formatAmount(data.Total, data.StdPrecision);
                            $mainNum.text(fmt.main);
                            $decimalNum.text('.' + fmt.decimal);
                            $whyText.text(
                                lbl('VAS_SumCreditLines', 'Sum of credit lines across the same')
                                + ' ' + data.JournalCount + ' '
                                + lbl('VAS_JournalEntriesPeriod', 'journal entries this period.')
                            );
                        } else {
                            $mainNum.text('—');
                            $decimalNum.text('');
                            $whyText.text(lbl('VIS_NoData', 'No data available.'));
                        }
                    } catch (e) {
                        $mainNum.text('—');
                        $decimalNum.text('');
                        $whyText.text(lbl('VIS_Error', 'Error loading data.'));
                    }
                    showBusy(false);
                },
                error: function () {
                    $mainNum.text('—');
                    $decimalNum.text('');
                    $whyText.text(lbl('VIS_Error', 'Error loading data.'));
                    showBusy(false);
                }
            });
        }

        this.refreshWidget    = function () { $mainNum.text('—'); $decimalNum.text(''); loadData(); };
        this.getRoot          = function () { return $root; };
        this.disposeComponent = function () { $root.remove(); };
    };

    VIS.GLJournalTotalCreditWidget.prototype.refreshWidget = function () {};

    VIS.GLJournalTotalCreditWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.GLJournalTotalCreditWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.GLJournalTotalCreditWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VIS, jQuery);
