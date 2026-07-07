/************************************************************
 * Module Name    : VAS
 * Purpose        : Aging Receivables Widget
 * chronological  : Development
 * Created Date   : 28 April 2026
 * Created by     : VAI154
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_AgingReceivablesWidget = function () {
        this.frame;
        this.windowNo;
        var $self = this;
        var $root;
        var $bsyDiv;
        var $container;
        var $body;
        var $emptyState;
        var widgetID = null;
        var data = null;

        var BUCKETS = [
            { key: "NotDueAmount",  msgKey: "VAS_NotYetDue",  cssTone: "tone-success" },
            { key: "Days_1_30",     msgKey: "VAS_Days1_30",   cssTone: "tone-warn" },
            { key: "Days_31_60",    msgKey: "VAS_Days31_60",  cssTone: "tone-warn-strong" },
            { key: "Days_61_90",    msgKey: "VAS_Days61_90",  cssTone: "tone-danger" },
            { key: "Over90",        msgKey: "VAS_Days90Plus", cssTone: "tone-danger-strong" }
        ];

        function fetchData() {
            $.ajax({
                url: VIS.Application.contextUrl + "VAS_AgingReceivablesWidget/GetAgingReceivables",
                dataType: "json",
                async: true,
                success: function (raw) {
                    var parsed = (typeof raw === "string") ? jQuery.parseJSON(raw) : raw;
                    data = normalize(parsed);
                    render();
                    showBusy(false);
                },
                error: function (err) {
                    console.log(err);
                    showBusy(false);
                }
            });
        }

        function normalize(parsed) {
            if (!parsed) return null;
            var p = {
                NotDueAmount:  +(parsed.NotDueAmount  || 0),
                Days_1_30:     +(parsed.Days_1_30     || 0),
                Days_31_60:    +(parsed.Days_31_60    || 0),
                Days_61_90:    +(parsed.Days_61_90    || 0),
                Days_91_120:   +(parsed.Days_91_120   || 0),
                Days_Over_120: +(parsed.Days_Over_120 || 0),
                StdPrecision:  +(parsed.StdPrecision  || 0),
                CurSymbol:     parsed.CurSymbol || "",
                ISO_Code:      parsed.ISO_Code  || ""
            };
            p.Over90 = p.Days_91_120 + p.Days_Over_120;
            return p;
        }

        function render() {
            $body.empty();

            if (!data) {
                $body.hide();
                $emptyState.show();
                return;
            }

            var max = 0;
            for (var i = 0; i < BUCKETS.length; i++) {
                var v = Math.abs(data[BUCKETS[i].key] || 0);
                if (v > max) max = v;
            }
            if (max === 0) max = 1;

            var anyValue = false;
            for (var j = 0; j < BUCKETS.length; j++) {
                var b = BUCKETS[j];
                var amt = data[b.key] || 0;
                if (amt !== 0) anyValue = true;

                var pct = Math.max(0, Math.min(100, (Math.abs(amt) / max) * 100));
                var amtTxt = formatShortAmount(amt, data.CurSymbol, data.ISO_Code);

                var $row = $(
                    '<div class="vas-aging-row">' +
                        '<div class="vas-aging-row-head">' +
                            '<span class="vas-aging-bucket-label"></span>' +
                            '<span class="vas-aging-bucket-amount"></span>' +
                        '</div>' +
                        '<div class="vas-aging-bar-track">' +
                            '<div class="vas-aging-bar-fill ' + b.cssTone + '"></div>' +
                        '</div>' +
                    '</div>'
                );
                $row.find(".vas-aging-bucket-label").text(VIS.Msg.getMsg(b.msgKey));
                $row.find(".vas-aging-bucket-amount").text(amtTxt);
                $row.find(".vas-aging-bar-fill").css("width", pct + "%");
                $body.append($row);
            }

            if (!anyValue) {
                $body.hide();
                $emptyState.show();
            } else {
                $emptyState.hide();
                $body.show();
            }
        }

        /** Format like $162k / $1.2M, falling back to fully formatted with precision when small. */
        function formatShortAmount(value, symbol, iso) {
            var sign = value < 0 ? "-" : "";
            var abs = Math.abs(value);
            var prefix = sign + (symbol || iso || "");
            if (abs >= 1.0e+12) return prefix + (abs / 1.0e+12).toFixed(1).replace(/\.0$/, "") + "T";
            if (abs >= 1.0e+9)  return prefix + (abs / 1.0e+9 ).toFixed(1).replace(/\.0$/, "") + "B";
            if (abs >= 1.0e+6)  return prefix + (abs / 1.0e+6 ).toFixed(1).replace(/\.0$/, "") + "M";
            if (abs >= 1.0e+3)  return prefix + (abs / 1.0e+3 ).toFixed(0) + "k";
            var precision = (data && data.StdPrecision) ? data.StdPrecision : 2;
            return prefix + abs.toLocaleString(window.navigator.language, {
                minimumFractionDigits: precision,
                maximumFractionDigits: precision
            });
        }

        function createBusyIndicator() {
            $bsyDiv = $('<div id="busyDivId_' + widgetID + '" class="vis-busyindicatorouterwrap">' +
                        '<div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($bsyDiv);
        }

        function showBusy(show) {
            if (!$bsyDiv) return;
            if (show) $bsyDiv.show(); else $bsyDiv.hide();
        }

        this.refreshWidget = function () {
            showBusy(true);
            data = null;
            fetchData();
        };

        this.sizeChanged = function (width) {
            // Layout adapts via CSS (em-based) — no JS resize work needed.
        };

        this.Initialize = function () {
            widgetID = this.widgetInfo.AD_UserHomeWidgetID;
            if (widgetID == 0) widgetID = $self.windowNo;

            $root = $('<div class="vas-aging-root vas-widget-bg" id="VAS_agingRoot_' + widgetID + '"></div>');

            $container = $(
                '<div class="vas-aging-container" id="vas_agingContainer_' + widgetID + '">' +
                    '<div class="vas-aging-header">' +
                        '<div class="vas-aging-header-icon">' +
                            '<i class="fa fa-clock-o" aria-hidden="true"></i>' +
                        '</div>' +
                        '<div class="vas-aging-header-text">' +
                            '<div class="vas-aging-title"></div>' +
                            '<div class="vas-aging-subtitle"></div>' +
                        '</div>' +
                    '</div>' +
                    '<div class="vas-aging-body" id="vas_agingBody_' + widgetID + '"></div>' +
                    '<div class="vas-aging-empty" id="vas_agingEmpty_' + widgetID + '" style="display:none;"></div>' +
                    '<div class="vas-aging-footer">' +
                        '<span class="vas-aging-why-pill"></span>' +
                        '<span class="vas-aging-why-text"></span>' +
                    '</div>' +
                '</div>'
            );

            $container.find(".vas-aging-title").text(VIS.Msg.getMsg("VAS_AgingReceivables"));
            $container.find(".vas-aging-subtitle").text(VIS.Msg.getMsg("VAS_AgingReceivablesSubtitle"));
            $container.find(".vas-aging-empty").text(VIS.Msg.getMsg("VAS_NoData"));
            $container.find(".vas-aging-why-pill").text(VIS.Msg.getMsg("VAS_Why"));
            $container.find(".vas-aging-why-text").text(VIS.Msg.getMsg("VAS_AgingReceivablesHint"));

            $root.append($container);
            $body       = $container.find("#vas_agingBody_" + widgetID);
            $emptyState = $container.find("#vas_agingEmpty_" + widgetID);

            createBusyIndicator();
            showBusy(true);
            fetchData();
        };

        this.getRoot = function () {
            return $root;
        };
    };

    VAS.VAS_AgingReceivablesWidget.prototype.widgetSizeChange = function (width) {
        this.sizeChanged(width);
    };

    VAS.VAS_AgingReceivablesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_AgingReceivablesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.Initialize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_AgingReceivablesWidget.prototype.dispose = function () {
        if (this.frame) this.frame.dispose();
        this.frame = null;
        this.windowNo = null;
    };

})(VAS, jQuery);
