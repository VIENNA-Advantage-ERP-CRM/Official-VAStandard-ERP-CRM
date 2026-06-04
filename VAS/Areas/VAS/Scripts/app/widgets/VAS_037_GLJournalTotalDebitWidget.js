/**
 * GL Journal Total Debit KPI Widget
 * Purpose  : Display the sum of AmtAcctDr from GL_JournalLine
 *            in the accounting schema base currency for month or YTD.
 * Tables   : GL_Journal, GL_JournalLine, C_AcctSchema, C_Currency
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    // ─── Messages & Labels used in this file ───────────────────────────────────
    // Messages : VIS_NoData, VIS_Error
    // Labels   : VAS_037_TotalDebit, VAS_037_Why, VAS_037_SumDebitLines,
    //            VAS_037_JournalEntriesPeriod, VAS_037_YTD
    // ───────────────────────────────────────────────────────────────────────────

    function lbl(key, fallback) {
        var t = VIS.Msg.getMsg(key);
        return (t && t.charAt(0) !== '[') ? t : fallback;
    }

    // Format a decimal amount into { main: '2,847,320', decimal: '00' }
    // Precision is always dynamic from C_Currency.StdPrecision — never hardcoded.
    function formatAmount(amount, precision) {
        var stdPrecision = VIS.Env.getCtx().getStdPrecision();
        var prec = (typeof precision === 'number' && precision >= 0) ? precision : stdPrecision;
        var val = parseFloat(amount || 0);
        var formatted = val.toLocaleString(window.navigator.language, {
            minimumFractionDigits: prec,
            maximumFractionDigits: prec
        });
        if (prec > 0) {
            return { main: formatted.slice(0, formatted.length - prec - 1), decimal: formatted.slice(-prec) };
        }
        return { main: formatted, decimal: '' };
    }

    // ──────────────────────────────────────────────────────────────────────────
    VAS.VAS_037_GLJournalTotalDebitWidget = function () {

        this.frame;
        this.windowNo;
        var $self        = this;
        var $root        = $('<div class="VAS-gljtd-root">');
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
            var $bsy = $('<div id="VAS-gljtd-busy-' + $self.AD_UserHomeWidgetID
                + '" class="vis-busyindicatorouterwrap">'
                + '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div>'
                + '</div>');
            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljtd-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            // Up-arrow SVG icon
            var svgIcon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"'
                + ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">'
                + '<line x1="12" y1="19" x2="12" y2="5"></line>'
                + '<polyline points="5 12 12 5 19 12"></polyline>'
                + '</svg>';

            var id = $self.AD_UserHomeWidgetID;

            var html = '<div class="kpi kpi-green">'

                // ── Header row ──────────────────────────────────────────────
                + '<div class="w-head">'
                +   '<div class="w-icon">' + svgIcon + '</div>'
                +   '<div class="w-title">' + lbl('VAS_037_TotalDebit', 'Total Debit') + '</div>'
                +   '<div class="VAS-gljtd-period-toggle" id="VAS-gljtd-toggle-' + id + '">'
                +     '<span class="VAS-gljtd-period VAS-gljtd-period-active" data-period="month"'
                +       ' id="VAS-gljtd-mon-' + id + '">—</span>'
                +     '<span class="VAS-gljtd-sep">&middot;</span>'
                +     '<span class="VAS-gljtd-period" data-period="ytd">'
                +       lbl('VAS_037_YTD', 'YTD')
                +     '</span>'
                +   '</div>'
                + '</div>'

                // ── KPI value ───────────────────────────────────────────────
                + '<div class="kpi-value" id="VAS-gljtd-val-' + id + '">'
                +   '<span class="VAS-gljtd-prefix" id="VAS-gljtd-pfx-' + id + '"></span>'
                +   '<span class="VAS-gljtd-main"   id="VAS-gljtd-main-' + id + '">—</span>'
                +   '<span class="VAS-gljtd-decimal" id="VAS-gljtd-dec-' + id + '"></span>'
                + '</div>'

                // ── Why section ─────────────────────────────────────────────
                + '<div class="kpi-why">'
                +   '<span class="kpi-why-label">' + lbl('VAS_037_Why', 'Why') + '</span>'
                +   '<span class="kpi-why-text" id="VAS-gljtd-why-' + id + '">&mdash;</span>'
                + '</div>'

                + '</div>'; // .kpi

            $root.append(html);

            $mainNum    = $root.find('#VAS-gljtd-main-' + id);
            $decimalNum = $root.find('#VAS-gljtd-dec-'  + id);
            $curPrefix  = $root.find('#VAS-gljtd-pfx-'  + id);
            $whyText    = $root.find('#VAS-gljtd-why-'  + id);

            // Period toggle click
            $root.find('#VAS-gljtd-toggle-' + id).on('click', '.VAS-gljtd-period', function () {
                var $btn = $(this);
                if ($btn.hasClass('VAS-gljtd-period-active')) { return; }
                $root.find('#VAS-gljtd-toggle-' + id)
                     .find('.VAS-gljtd-period')
                     .removeClass('VAS-gljtd-period-active');
                $btn.addClass('VAS-gljtd-period-active');
                activePeriod = $btn.data('period');
                showBusy(true);
                loadData();
            });
        }

        function loadData() {
            $.ajax({
                url      : baseUrl + 'VAS/VAS_037_GLJournalTotalDebitWidget/GetTotalDebit',
                type     : 'GET',
                dataType : 'json',
                cache    : false,
                data     : { period: activePeriod },
                success  : function (result) {
                    try {
                        var data = JSON.parse(result);
                        if (data) {
                            // Update month label in toggle
                            $root.find('[data-period="month"]').text(data.MonthAbbr || '—');

                            // Currency prefix (from schema — no hardcoded symbol)
                            $curPrefix.text(data.CurSymbol || data.ISOCode || '');

                            // Format and split amount
                            var fmt = formatAmount(data.Total, data.StdPrecision);
                            $mainNum.text(fmt.main);
                            $decimalNum.text('.' + fmt.decimal);

                            // Why text
                            $whyText.text(
                                lbl('VAS_037_SumDebitLines', 'Sum of debit lines across')
                                + ' ' + data.JournalCount + ' '
                                + lbl('VAS_037_JournalEntriesPeriod', 'journal entries this period.')
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

    VAS.VAS_037_GLJournalTotalDebitWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_037_GLJournalTotalDebitWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_037_GLJournalTotalDebitWidget.prototype.widgetSizeChange = function (height, width) {};

    VAS.VAS_037_GLJournalTotalDebitWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
