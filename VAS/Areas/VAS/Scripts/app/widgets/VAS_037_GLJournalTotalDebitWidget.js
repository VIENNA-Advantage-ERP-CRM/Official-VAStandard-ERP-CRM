/**
 * GL Journal Total Debit KPI Widget
 * Purpose - Display the sum of AmtAcctDr from GL_JournalLine
 * in the accounting schema base currency for month or YTD.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Total Debit                          | VAS_037_TotalDebit
 *  2  | YTD                                  | VAS_037_YTD
 *  3  | Sum of debit lines across            | VAS_037_SumDebitLines
 *  4  | journal entries this period.         | VAS_037_JournalEntriesPeriod
 *  5  | No data available.                   | VIS_NoData
 *  6  | Error loading data.                  | VIS_Error
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_037_GLJournalTotalDebitWidget = function () {

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $root = $('<div class="VAS-gljtd-root">');
        var $kpiValue = null;
        var $whyText = null;
        var refreshTimer = null;
        var activePeriod = 'month';
        var baseUrl = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            loadData();

            refreshTimer = setInterval(function () {
                $self.refreshWidget();
            }, 1000 * 60 * 5);
        };

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO) {
            var numericValue = Number(value || 0);
            var stdPrecision = 2;

            if (VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                stdPrecision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            if (isNaN(stdPrecision) || stdPrecision < 0) {
                stdPrecision = 2;
            }

            return numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: stdPrecision,
                maximumFractionDigits: stdPrecision
            });
        }

        function createBusyIndicator() {
            var $bsy = $(
                '<div id="VAS-gljtd-busy-' + $self.AD_UserHomeWidgetID + '" class="vis-busyindicatorouterwrap">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>'
            );

            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljtd-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            var id = $self.AD_UserHomeWidgetID;

            var svgIcon =
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"' +
                ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<line x1="12" y1="19" x2="12" y2="5"></line>' +
                '<polyline points="5 12 12 5 19 12"></polyline>' +
                '</svg>';

            var html =
                '<div class="kpi kpi-green">' +
                '<div class="w-head">' +
                '<div class="w-icon">' + svgIcon + '</div>' +
                '<div class="w-title">' + lbl('VAS_037_TotalDebit', 'Total Debit') + '</div>' +
                '<div class="VAS-gljtd-period-toggle" id="VAS-gljtd-toggle-' + id + '">' +
                '<span class="VAS-gljtd-period VAS-gljtd-period-active" data-period="month" id="VAS-gljtd-mon-' + id + '">—</span>' +
                '<span class="VAS-gljtd-sep">&middot;</span>' +
                '<span class="VAS-gljtd-period" data-period="ytd">' + lbl('VAS_037_YTD', 'YTD') + '</span>' +
                '</div>' +
                '</div>' +
                '<div class="kpi-value" id="VAS-gljtd-val-' + id + '">&mdash;</div>' +
                '<div class="kpi-why">' +
                '<span class="kpi-why-text" id="VAS-gljtd-why-' + id + '">&mdash;</span>' +
                '</div>' +
                '</div>';

            $root.append(html);

            $kpiValue = $root.find('#VAS-gljtd-val-' + id);
            $whyText = $root.find('#VAS-gljtd-why-' + id);

            $root.find('#VAS-gljtd-toggle-' + id).on('click', '.VAS-gljtd-period', function () {
                var $btn = $(this);

                if ($btn.hasClass('VAS-gljtd-period-active')) {
                    return;
                }

                $root.find('#VAS-gljtd-toggle-' + id)
                    .find('.VAS-gljtd-period')
                    .removeClass('VAS-gljtd-period-active');

                $btn.addClass('VAS-gljtd-period-active');

                activePeriod = $btn.data('period') || 'month';
                loadData();
            });
        }

        function normalizeResponse(result) {
            if (!result) {
                return null;
            }

            if (typeof result === 'string') {
                try {
                    return JSON.parse(result);
                } catch (e) {
                    return null;
                }
            }

            return result;
        }

        function renderEmpty(message) {
            $kpiValue.text('—');
            $whyText.text(message || lbl('VIS_NoData', 'No data available.'));
        }

        function renderData(data) {
            var monthText = data.MonthAbbr || data.badgeText || '—';
            var amountValue = data.Total;

            if (amountValue === undefined || amountValue === null) {
                amountValue = data.mainMetric || 0;
            }

            $root.find('#VAS-gljtd-mon-' + $self.AD_UserHomeWidgetID).text(monthText);

            var amountText = formatCurrencyAmount(amountValue, data.currencySymbol || data.CurSymbol, data.currencyISO || data.ISOCode);

            $kpiValue.text(amountText);

            $whyText.text(
                lbl('VAS_037_SumDebitLines', 'Sum of debit lines across') +
                ' ' +
                Number(data.JournalCount || 0).toLocaleString(window.navigator.language) +
                ' ' +
                lbl('VAS_037_JournalEntriesPeriod', 'journal entries this period.')
            );
        }

        function loadData() {
            showBusy(true);

            $.ajax({
                url: baseUrl + 'VAS/VAS_037_GLJournalTotalDebitWidget/GetTotalDebit',
                type: 'GET',
                dataType: 'json',
                cache: false,
                data: {
                    period: activePeriod
                },
                success: function (result) {
                    var data = normalizeResponse(result);

                    if (!data) {
                        renderEmpty(lbl('VIS_Error', 'Error loading data.'));
                        return;
                    }

                    if (data.success === false || data.error) {
                        renderEmpty(data.error || lbl('VIS_Error', 'Error loading data.'));
                        return;
                    }

                    if (data.hasData === false) {
                        renderEmpty(lbl('VIS_NoData', 'No data available.'));
                        return;
                    }

                    renderData(data);
                },
                error: function () {
                    renderEmpty(lbl('VIS_Error', 'Error loading data.'));
                },
                complete: function () {
                    showBusy(false);
                }
            });
        }

        this.refreshWidget = function () {
            if ($kpiValue) {
                $kpiValue.text('—');
            }

            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (refreshTimer) {
                clearInterval(refreshTimer);
                refreshTimer = null;
            }

            if ($root) {
                $root.off();
                $root.remove();
            }

            $kpiValue = null;
            $whyText = null;
            $root = null;
            $self = null;
        };
    };

    VAS.VAS_037_GLJournalTotalDebitWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_037_GLJournalTotalDebitWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_037_GLJournalTotalDebitWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);