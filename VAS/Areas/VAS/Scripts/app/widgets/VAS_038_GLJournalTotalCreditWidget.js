/**
 * GL Journal Total Credit KPI Widget
 * Purpose - Display the sum of AmtAcctCr from GL_JournalLine
 * in the accounting schema base currency for month or YTD.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Total Credit                         | VAS_038_TotalCredit
 *  2  | YTD                                  | VAS_038_YTD
 *  3  | Sum of credit lines across           | VAS_038_SumCreditLines
 *  4  | journal entries this period.         | VAS_038_JournalEntriesPeriod
 *  5  | No data available.                   | VIS_NoData
 *  6  | Error loading data.                  | VIS_Error
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_038_GLJournalTotalCreditWidget = function () {

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $root = $('<div class="VAS-gljtc-root">');
        var $kpiValue = null;
        var $whyText = null;
        var activePeriod = 'month';
        var baseUrl = VIS.Application.contextUrl;

        this.Initalize = function () {
            createWidget();
            createBusyIndicator();
            loadData();
        };

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        function esc(value) {
            return String(value == null ? "" : value)
                .replace(/&/g, "&amp;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;")
                .replace(/"/g, "&quot;")
                .replace(/'/g, "&#039;");
        }

        function getResponseMessage(data, fallback) {
            var key = data && (data.errorKey || data.messageKey);

            if (key) {
                return lbl(key, data.error || data.errorText || data.message || fallback);
            }

            return data && (data.error || data.errorText || data.message) || fallback;
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

        function getCurrencyText(data) {
            return data.currencySymbol ||
                data.CurSymbol ||
                data.currencyISO ||
                data.ISOCode ||
                "";
        }

        function createBusyIndicator() {
            var $bsy = $(
                '<div id="VAS-gljtc-busy-' + $self.AD_UserHomeWidgetID + '" class="vis-busyindicatorouterwrap">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>'
            );

            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljtc-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            var id = $self.AD_UserHomeWidgetID;

            var svgIcon =
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"' +
                ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<line x1="12" y1="5" x2="12" y2="19"></line>' +
                '<polyline points="5 12 12 19 19 12"></polyline>' +
                '</svg>';

            var html =
                '<div class="kpi kpi-blue">' +
                '<div class="w-head">' +
                '<div class="w-icon">' + svgIcon + '</div>' +
                '<div class="w-title">' + lbl('VAS_038_TotalCredit', 'Total Credit') + '</div>' +
                '<div class="VAS-gljtc-period-toggle" id="VAS-gljtc-toggle-' + id + '">' +
                '<span class="VAS-gljtc-period VAS-gljtc-period-active" data-period="month" id="VAS-gljtc-mon-' + id + '">—</span>' +
                '<span class="VAS-gljtc-sep">&middot;</span>' +
                '<span class="VAS-gljtc-period" data-period="ytd">' + lbl('VAS_038_YTD', 'YTD') + '</span>' +
                '</div>' +
                '</div>' +

                '<div class="kpi-value" id="VAS-gljtc-val-' + id + '">&mdash;</div>' +

                '<div class="kpi-why">' +
                '<span class="kpi-why-text" id="VAS-gljtc-why-' + id + '">&mdash;</span>' +
                '</div>' +
                '</div>';

            $root.append(html);

            $kpiValue = $root.find('#VAS-gljtc-val-' + id);
            $whyText = $root.find('#VAS-gljtc-why-' + id);

            $root.find('#VAS-gljtc-toggle-' + id).on('click', '.VAS-gljtc-period', function () {
                var $btn = $(this);

                if ($btn.hasClass('VAS-gljtc-period-active')) {
                    return;
                }

                $root.find('#VAS-gljtc-toggle-' + id)
                    .find('.VAS-gljtc-period')
                    .removeClass('VAS-gljtc-period-active');

                $btn.addClass('VAS-gljtc-period-active');

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

            $root.find('#VAS-gljtc-mon-' + $self.AD_UserHomeWidgetID).text(monthText);

            var amountText = formatCurrencyAmount(
                amountValue,
                data.currencySymbol || data.CurSymbol,
                data.currencyISO || data.ISOCode
            );

            var currencyText = getCurrencyText(data);

            $kpiValue.html(
                (
                    currencyText
                        ? '<span class="VAS-gljtc-prefix">' +
                        esc(currencyText) +
                        '</span>'
                        : ''
                ) +
                '<span>' +
                esc(amountText) +
                '</span>'
            );

            $whyText.text(
                lbl('VAS_038_SumCreditLines', 'Sum of credit lines across') +
                ' ' +
                Number(data.JournalCount || 0).toLocaleString(window.navigator.language) +
                ' ' +
                lbl('VAS_038_JournalEntriesPeriod', 'journal entries this period.')
            );
        }

        function loadData() {
            showBusy(true);

            $.ajax({
                url: baseUrl + 'VAS/VAS_037_GLJournalTotalDebitWidget/GetTotalCredit',
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
                        renderEmpty(getResponseMessage(data, lbl('VIS_Error', 'Error loading data.')));
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

    VAS.VAS_038_GLJournalTotalCreditWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_038_GLJournalTotalCreditWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_038_GLJournalTotalCreditWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
