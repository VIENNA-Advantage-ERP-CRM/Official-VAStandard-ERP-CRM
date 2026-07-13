/**
 * GL Journal Net Difference KPI Widget
 * Purpose - Display SUM(AmtAcctDr) - SUM(AmtAcctCr) from GL_JournalLine
 * in the accounting schema base currency for month or YTD.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Net Difference                       | VAS_040_NetDifference
 *  2  | DR − CR                              | VAS_040_DrMinusCr
 *  3  | YTD                                  | VAS_040_YTD
 *  4  | Books are in balance                 | VAS_040_BooksInBalance
 *  5  | Debit exceeds credit.                | VAS_040_DebitExceedsCredit
 *  6  | Credit exceeds debit.                | VAS_040_CreditExceedsDebit
 *  7  | No data available.                   | VIS_NoData
 *  8  | Error loading data.                  | VIS_Error
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_040_GLJournalNetDiffWidget = function () {

        this.frame = null;
        this.windowNo = 0;
        this.AD_UserHomeWidgetID = 0;

        var $self = this;
        var $root = $('<div class="VAS-gljnd-root">');
        var $kpiValue = null;
        var $statusText = null;
        var $valueWrap = null;
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

        function getCurrencyText(data) {
            return data.currencySymbol ||
                data.CurSymbol ||
                data.currencyISO ||
                data.ISOCode ||
                "";
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO) {
            var numericValue = Math.abs(Number(value || 0));
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
                '<div id="VAS-gljnd-busy-' + $self.AD_UserHomeWidgetID + '" class="vis-busyindicatorouterwrap">' +
                '<div class="vis-busyindicatorinnerwrap">' +
                '<i class="vis_widgetloader"></i>' +
                '</div>' +
                '</div>'
            );

            $root.append($bsy);
        }

        function showBusy(show) {
            var $b = $root.find('#VAS-gljnd-busy-' + $self.AD_UserHomeWidgetID);
            if (show) { $b.show(); } else { $b.hide(); }
        }

        function createWidget() {
            var id = $self.AD_UserHomeWidgetID;

            var svgIcon =
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor"' +
                ' stroke-width="2" stroke-linecap="round" stroke-linejoin="round">' +
                '<path d="M16 3h5v5M4 20L21 3M21 16v5h-5M15 15l6 6M4 4l5 5"></path>' +
                '</svg>';

            var html =
                '<div class="kpi kpi-teal">' +
                '<div class="w-head">' +
                '<div class="w-icon">' + svgIcon + '</div>' +
                '<div class="w-title">' + lbl('VAS_040_NetDifference', 'Net Difference') + '</div>' +
                '<div class="VAS-gljnd-period-toggle" id="VAS-gljnd-toggle-' + id + '">' +
                '<span class="VAS-gljnd-period VAS-gljnd-period-active" data-period="month" id="VAS-gljnd-mon-' + id + '">—</span>' +
                '<span class="VAS-gljnd-sep">&middot;</span>' +
                '<span class="VAS-gljnd-period" data-period="ytd">' + lbl('VAS_040_YTD', 'YTD') + '</span>' +
                '</div>' +
                '<span class="VAS-gljnd-drcr">' + lbl('VAS_040_DrMinusCr', 'DR − CR') + '</span>' +
                '</div>' +

                '<div class="kpi-value" id="VAS-gljnd-val-' + id + '">&mdash;</div>' +

                '<div class="kpi-why">' +
                '<span class="kpi-why-text VAS-gljnd-status" id="VAS-gljnd-status-' + id + '">&mdash;</span>' +
                '</div>' +
                '</div>';

            $root.append(html);

            $kpiValue = $root.find('#VAS-gljnd-val-' + id);
            $statusText = $root.find('#VAS-gljnd-status-' + id);
            $valueWrap = $root.find('#VAS-gljnd-val-' + id);

            $root.find('#VAS-gljnd-toggle-' + id).on('click', '.VAS-gljnd-period', function () {
                var $btn = $(this);

                if ($btn.hasClass('VAS-gljnd-period-active')) {
                    return;
                }

                $root.find('#VAS-gljnd-toggle-' + id)
                    .find('.VAS-gljnd-period')
                    .removeClass('VAS-gljnd-period-active');

                $btn.addClass('VAS-gljnd-period-active');

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

            if ($valueWrap) {
                $valueWrap.removeClass('VAS-gljnd-balanced VAS-gljnd-unbalanced');
            }

            if ($statusText) {
                $statusText
                    .removeClass('VAS-gljnd-status-ok VAS-gljnd-status-warn')
                    .text(message || lbl('VIS_NoData', 'No data available.'));
            }
        }

        function renderData(data) {
            var monthText = data.MonthAbbr || data.badgeText || '—';
            var netDiff = Number(data.NetDiff);

            if (isNaN(netDiff)) {
                netDiff = Number(data.mainMetric || 0);
            }

            $root.find('#VAS-gljnd-mon-' + $self.AD_UserHomeWidgetID).text(monthText);

            var amountText = formatCurrencyAmount(
                netDiff,
                data.currencySymbol || data.CurSymbol,
                data.currencyISO || data.ISOCode
            );

            var currencyText = getCurrencyText(data);

            $kpiValue.html(
                (
                    currencyText
                        ? '<span class="VAS-gljnd-prefix">' + esc(currencyText) + '</span>'
                        : ''
                ) +
                '<span>' + esc(amountText) + '</span>'
            );

            $valueWrap.removeClass('VAS-gljnd-balanced VAS-gljnd-unbalanced');
            $statusText.removeClass('VAS-gljnd-status-ok VAS-gljnd-status-warn');

            if (data.IsBalanced === true || netDiff === 0) {
                $valueWrap.addClass('VAS-gljnd-balanced');
                $statusText.addClass('VAS-gljnd-status-ok');
                $statusText.text(lbl('VAS_040_BooksInBalance', 'Books are in balance \u00B7 ledger ready for posting.'));
            } else if (netDiff > 0) {
                $valueWrap.addClass('VAS-gljnd-unbalanced');
                $statusText.addClass('VAS-gljnd-status-warn');
                $statusText.text(lbl('VAS_040_DebitExceedsCredit', 'Debit exceeds credit.'));
            } else {
                $valueWrap.addClass('VAS-gljnd-unbalanced');
                $statusText.addClass('VAS-gljnd-status-warn');
                $statusText.text(lbl('VAS_040_CreditExceedsDebit', 'Credit exceeds debit.'));
            }
        }

        function loadData() {
            showBusy(true);

            $.ajax({
                url: baseUrl + 'VAS/VAS_037_GLJournalTotalDebitWidget/GetNetDifference',
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
            if (refreshTimer) {
                clearInterval(refreshTimer);
                refreshTimer = null;
            }

            if ($root) {
                $root.off();
                $root.remove();
            }

            $kpiValue = null;
            $statusText = null;
            $valueWrap = null;
            $root = null;
            $self = null;
        };
    };

    VAS.VAS_040_GLJournalNetDiffWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_040_GLJournalNetDiffWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_040_GLJournalNetDiffWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
