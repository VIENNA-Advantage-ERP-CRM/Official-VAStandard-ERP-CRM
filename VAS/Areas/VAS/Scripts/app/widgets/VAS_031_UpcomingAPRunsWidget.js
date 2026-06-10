/**
 * Upcoming runs
 * Purpose - Displays upcoming AP payment runs due within the next 7 days, grouped by payment method and due date.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                         | Message Key
 * ----+--------------------------------------+--------------------------------
 *  1  | Upcoming runs                        | VAS_031_MessageUpcomingRuns
 *  2  | Next 7 days                          | VAS_031_MessageNext7Days
 *  3  | Batch                                | VAS_031_MessageBatch
 *  4  | payment                              | VAS_031_MessagePayment
 *  5  | payments                             | VAS_031_MessagePayments
 *  6  | Loading                              | VAS_031_MessageLoading
 *  7  | No Data                              | VAS_031_MessageNoData
 * ─────────────────────────────────────────────────────────────────────
 */

; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_031_UpcomingAPRunsWidget = function () {

        this.frame;
        this.windowNo;
        this.AD_UserHomeWidgetID;

        var $root = $('<div class="vas-upcoming-ap-runs-root">');
        var $card;
        var $body;

        function lbl(key, fallback) {
            var text = VIS.Msg.getMsg(key);
            return text && text !== '[' + key + ']' ? text : fallback;
        }

        this.Initalize = function () {
            createWidget();
            loadData();
        };

        function createWidget() {
            $card = $('<div class="vas-upcoming-ap-runs-card">');

            var $head = $('<div class="vas-upcoming-ap-runs-head">');
            var $headLeft = $('<div class="vas-upcoming-ap-runs-head-left">');
            var $titleRow = $('<div class="vas-upcoming-ap-runs-title-row">');
            var $iconBox = $('<span class="vas-upcoming-ap-runs-icon-box">');
            var $icon = $(
                '<svg class="vas-upcoming-ap-runs-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false">' +
                '<circle cx="12" cy="12" r="10"></circle>' +
                '<polyline points="12 6 12 12 16 14"></polyline>' +
                '</svg>'
            );

            var $title = $('<div class="vas-upcoming-ap-runs-title">').text(lbl('VAS_031_MessageUpcomingRuns', 'Upcoming runs'));
            var $sub = $('<div class="vas-upcoming-ap-runs-sub">').text(lbl('VAS_031_MessageNext7Days', 'Next 7 days'));

            $iconBox.append($icon);
            $titleRow.append($iconBox).append($title);
            $headLeft.append($titleRow).append($sub);
            $head.append($headLeft);

            $body = $('<div class="vas-upcoming-ap-runs-body">');

            $card.append($head).append($body);
            $root.empty().append($card);
        }

        function loadData() {
            setLoading();

            $.ajax({
                url: VIS.Application.contextUrl + 'VAS_033_UpcomingAPRunsWidget/GetUpcomingAPRuns',
                type: 'GET',
                dataType: 'json',
                cache: false,
                success: function (response) {
                    var data = response;

                    if (typeof response === 'string') {
                        try {
                            data = JSON.parse(response);
                        }
                        catch (e) {
                            setNoData();
                            return;
                        }
                    }

                    if (!data || data.error) {
                        setNoData();
                        return;
                    }

                    renderData(data);
                },
                error: function () {
                    setNoData();
                }
            });
        }

        function renderData(data) {
            var runs = $.isArray(data.runs) ? data.runs : [];

            if (runs.length === 0) {
                setNoData();
                return;
            }

            $body.empty();

            for (var i = 0; i < runs.length && i < 3; i++) {
                $body.append(createRunRow(runs[i]));
            }
        }

        function createRunRow(run) {
            var paymentMethodName = run.paymentMethodName || lbl('VAS_031_MessageNotSpecified', 'Not Specified');
            var paymentCount = Number(run.paymentCount || 0);
            var amount = Number(run.totalAmount || 0);
            var dueDateText = run.dueDateText || formatDate(run.dueDate);
            var titleText = getRunTitle(paymentMethodName, run.vendorName, paymentCount);
            var metaText = dueDateText + ' · ' + paymentCount.toLocaleString(window.navigator.language) + ' ' + getPaymentLabel(paymentCount);
            var barClass = getPaymentMethodClass(paymentMethodName);

            var $row = $('<div class="vas-upcoming-ap-runs-row">');
            var $bar = $('<span class="vas-upcoming-ap-runs-bar">').addClass(barClass);
            var $info = $('<div class="vas-upcoming-ap-runs-info">');
            var $title = $('<div class="vas-upcoming-ap-runs-run-title">').text(titleText);
            var $meta = $('<div class="vas-upcoming-ap-runs-meta">').text(metaText);
            var $amount = $('<span class="vas-upcoming-ap-runs-amount">').text(formatCurrencyAmount(amount, run.currencySymbol, run.currencyISO, run.stdPrecision));

            $info.append($title).append($meta);
            $row.append($bar).append($info).append($amount);

            return $row;
        }

        function getRunTitle(paymentMethodName, vendorName, paymentCount) {
            if (paymentCount === 1 && vendorName) {
                return paymentMethodName + ' · ' + vendorName;
            }

            return paymentMethodName + ' ' + lbl('VAS_031_MessageBatch', 'Batch');
        }

        function getPaymentLabel(paymentCount) {
            return paymentCount === 1
                ? lbl('VAS_031_MessagePayment', 'payment')
                : lbl('VAS_031_MessagePayments', 'payments');
        }

        function getPaymentMethodClass(paymentMethodName) {
            var method = (paymentMethodName || '').toLowerCase();

            if (method.indexOf('rtgs') >= 0) {
                return 'vas-upcoming-ap-runs-bar-rtgs';
            }

            if (method.indexOf('upi') >= 0) {
                return 'vas-upcoming-ap-runs-bar-upi';
            }

            if (method.indexOf('card') >= 0) {
                return 'vas-upcoming-ap-runs-bar-card';
            }

            if (method.indexOf('cheque') >= 0 || method.indexOf('check') >= 0) {
                return 'vas-upcoming-ap-runs-bar-cheque';
            }

            return 'vas-upcoming-ap-runs-bar-neft';
        }

        function formatDate(value) {
            if (!value) {
                return '';
            }

            var date = new Date(value);

            if (typeof value === 'string' && value.indexOf('/Date(') === 0) {
                var timestamp = Number(value.replace(/[^0-9-]/g, ''));
                date = new Date(timestamp);
            }

            if (isNaN(date.getTime())) {
                return value;
            }

            return date.toLocaleDateString(window.navigator.language, {
                weekday: 'short',
                day: '2-digit',
                month: 'short'
            });
        }

        function formatCurrencyAmount(value, currencySymbol, currencyISO, stdPrecision) {
            var numericValue = Number(value || 0);
            var precision = Number(stdPrecision);

            if (isNaN(precision) && VIS && VIS.Env && VIS.Env.getCtx && VIS.Env.getCtx().getStdPrecision) {
                precision = Number(VIS.Env.getCtx().getStdPrecision());
            }

            if (isNaN(precision) || precision < 0) {
                precision = 2;
            }

            var amount = numericValue.toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });

            if (currencySymbol) {
                return currencySymbol + amount;
            }

            return currencyISO ? amount + ' ' + currencyISO : amount;
        }

        function setLoading() {
            if ($body) {
                $body.empty().append($('<div class="vas-upcoming-ap-runs-state">').text(lbl('VAS_031_MessageLoading', 'Loading')));
            }
        }

        function setNoData() {
            if ($body) {
                $body.empty().append($('<div class="vas-upcoming-ap-runs-state">').text(lbl('VAS_031_MessageNoData', 'No Data')));
            }
        }

        this.refreshWidget = function () {
            loadData();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
            $card = null;
            $body = null;
        };
    };

    VAS.VAS_031_UpcomingAPRunsWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_031_UpcomingAPRunsWidget.prototype.widgetSizeChange = function (height, width) {
    };

    VAS.VAS_031_UpcomingAPRunsWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_031_UpcomingAPRunsWidget.prototype.dispose = function () {
        this.disposeComponent();

        if (this.frame) {
            this.frame.dispose();
        }

        this.frame = null;
    };

})(VAS, jQuery);
