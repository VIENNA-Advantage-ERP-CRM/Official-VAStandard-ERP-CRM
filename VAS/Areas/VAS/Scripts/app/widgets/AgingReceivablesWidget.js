/**
 * Aging Receivables Widget
 * Purpose - Table showing outstanding sales orders with delivery/invoice status and aging.
 *
 * ── Labels / Message Keys ──────────────────────────────────────────────────────────────
 *  #  | Current Text                                    | Message Key                      | MsgText
 * ----+-------------------------------------------------+----------------------------------+-------------------------------------------------
 *  1  | Aging Receivables                               | VIS_AgingReceivables             | Aging Receivables
 *  2  | Who owes, how old                               | VIS_WhoOwesHowOld                | Who owes, how old
 *  3  | #                                               | VIS_ColNo                        | #
 *  4  | Order No                                        | VIS_ColOrderNo                   | Order No
 *  5  | Customer                                        | VIS_Customer                     | Customer
 *  6  | Delivery                                        | VIS_ColDelivery                  | Delivery
 *  7  | Invoice                                         | VIS_Invoice                      | Invoice
 *  8  | Value                                           | VIS_ColValue                     | Value
 *  9  | Pending                                         | VIS_ColPending                   | Pending
 * 10  | Loading…                                        | VIS_Loading                      | Loading…
 * 11  | No data                                         | VIS_NoData                       | No data
 * 12  | WHY                                             | VIS_Why                          | WHY
 * 13  | Older invoices are harder to collect...         | VIS_AgingWhyText                 | Older invoices are harder to collect. Focus on the 61+ buckets.
 * 14  | Full                                            | VIS_StatusFull                   | Full
 * 15  | Partial                                         | VIS_StatusPartial                | Partial
 * 16  | Partial Raised                                  | VIS_StatusPartialRaised          | Partial Raised
 * 17  | Not Delivered                                   | VIS_StatusNotDelivered           | Not Delivered
 * 18  | Not Raised                                      | VIS_StatusNotRaised              | Not Raised
 * ──────────────────────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.AgingReceivablesWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div style="height:100%;font-family:Roboto,sans-serif;">');

        var $tbody;

        function lbl(key, fallback) {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            loadData();
        };

        /* ── Load data from backend ── */
        function loadData() {
            $.ajax({
                url: VIS.Application.contextUrl + 'AgingReceivables/GetAgingReceivables',
                type: 'GET',
                success: function (res) {
                    var data = typeof res === 'string' ? JSON.parse(res) : res;
                    if (Array.isArray(data)) {
                        renderRows(data);
                    }
                },
                error: function () { /* leave loading placeholder on error */ }
            });
        }

        /* ── Format currency ── */
        function formatCurrency(value) {
            if (value >= 1000000) {
                return '$' + (value / 1000000).toFixed(1).replace(/\.0$/, '') + 'M';
            }
            if (value >= 1000) {
                return '$' + Math.round(value / 1000) + 'k';
            }
            return '$' + Math.round(value).toLocaleString('en-US');
        }

        /* ── Status chip ── */
        function statusChip(text) {
            var bg, color, label;
            switch (text) {
                case 'Full':
                    bg = '#CCEFDD'; color = '#0C5D38'; label = lbl("VIS_StatusFull",          text); break;
                case 'Partial':
                    bg = '#FFF3CD'; color = '#7A5000'; label = lbl("VIS_StatusPartial",       text); break;
                case 'Partial Raised':
                    bg = '#FFF3CD'; color = '#7A5000'; label = lbl("VIS_StatusPartialRaised", text); break;
                case 'Not Delivered':
                    bg = '#FAD7D7'; color = '#8F2D2D'; label = lbl("VIS_StatusNotDelivered",  text); break;
                case 'Not Raised':
                    bg = '#FAD7D7'; color = '#8F2D2D'; label = lbl("VIS_StatusNotRaised",     text); break;
                default:
                    bg = '#E1E1E1'; color = '#505050';  label = text;
            }
            return '<span style="' +
                'display:inline-block;padding:2px 8px;border-radius:999px;' +
                'font-size:10px;font-weight:600;white-space:nowrap;' +
                'background:' + bg + ';color:' + color + ';' +
            '">' + label + '</span>';
        }

        /* ── Render table rows ── */
        function renderRows(rows) {
            if (!$tbody) return;
            $tbody.empty();

            if (rows.length === 0) {
                $tbody.append(
                    '<tr><td colspan="7" style="text-align:center;padding:16px;color:#748494;font-size:12px;">' + lbl("VIS_NoData", 'No data') + '</td></tr>'
                );
                return;
            }

            for (var i = 0; i < rows.length; i++) {
                var r = rows[i];
                var $tr = $(
                    '<tr style="border-bottom:1px solid #EDF2F6;">' +
                        '<td style="padding:8px 10px;font-size:12px;color:#748494;">' + r.srNo + '</td>' +
                        '<td style="padding:8px 10px;font-size:12px;font-weight:600;color:#102C3F;">' + (r.orderNo || '—') + '</td>' +
                        '<td style="padding:8px 10px;font-size:12px;color:#102C3F;">' + (r.customerName || '—') + '</td>' +
                        '<td style="padding:8px 10px;">' + statusChip(r.deliveryStatus) + '</td>' +
                        '<td style="padding:8px 10px;">' + statusChip(r.invoiceStatus) + '</td>' +
                        '<td style="padding:8px 10px;font-size:12px;font-weight:600;color:#102C3F;text-align:right;font-variant-numeric:tabular-nums;">' + formatCurrency(r.orderValue) + '</td>' +
                        '<td style="padding:8px 10px;font-size:12px;color:#748494;text-align:right;">' + (r.daysPending || '—') + '</td>' +
                    '</tr>'
                );
                $tbody.append($tr);
            }
        }

        /* ── Build DOM ── */
        function createWidget() {
            var $card = $(
                '<div style="' +
                    'background:linear-gradient(180deg,rgba(255,255,255,0.82) 0%,rgba(255,255,255,0.58) 100%);' +
                    'border:2px solid #fff;' +
                    'border-radius:14px;' +
                    'box-shadow:0 10px 24px rgba(15,61,97,0.06);' +
                    'padding:16px 18px 18px;' +
                    'height:100%;' +
                    'box-sizing:border-box;' +
                    'display:flex;flex-direction:column;' +
                '">'
            );

            /* ── Header ── */
            var $header = $(
                '<div style="display:flex;align-items:flex-start;gap:10px;margin-bottom:14px;">' +
                    '<div style="' +
                        'width:36px;height:36px;border-radius:8px;flex-shrink:0;' +
                        'background:oklch(0.96 0.03 220);' +
                        'display:flex;align-items:center;justify-content:center;' +
                    '">' +
                        '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" ' +
                            'stroke="oklch(0.45 0.15 220)" stroke-width="1.6" ' +
                            'stroke-linecap="round" stroke-linejoin="round">' +
                            '<circle cx="12" cy="12" r="10"/>' +
                            '<polyline points="12 6 12 12 16 14"/>' +
                        '</svg>' +
                    '</div>' +
                    '<div>' +
                        '<div style="font-size:13px;font-weight:600;color:#102C3F;line-height:1.2;">' + lbl("VIS_AgingReceivables", 'Aging Receivables') + '</div>' +
                        '<div style="font-size:11px;color:#748494;letter-spacing:0.3px;text-transform:uppercase;margin-top:1px;">' + lbl("VIS_WhoOwesHowOld", 'Who owes, how old') + '</div>' +
                    '</div>' +
                '</div>'
            );

            /* ── Scrollable table ── */
            var $scroll = $(
                '<div style="flex:1;overflow-y:auto;margin:0 -2px;">'
            );

            var $table = $(
                '<table style="width:100%;border-collapse:collapse;">'
            );

            var $thead = $(
                '<thead>' +
                    '<tr style="border-bottom:1px solid #E4EDF4;">' +
                        '<th style="padding:6px 10px;font-size:11px;font-weight:400;color:#748494;text-align:left;">'  + lbl("VIS_ColNo",       '#')        + '</th>' +
                        '<th style="padding:6px 10px;font-size:11px;font-weight:400;color:#748494;text-align:left;">'  + lbl("VIS_ColOrderNo",  'Order No') + '</th>' +
                        '<th style="padding:6px 10px;font-size:11px;font-weight:400;color:#748494;text-align:left;">'  + lbl("VIS_Customer",    'Customer') + '</th>' +
                        '<th style="padding:6px 10px;font-size:11px;font-weight:400;color:#748494;text-align:left;">'  + lbl("VIS_ColDelivery", 'Delivery') + '</th>' +
                        '<th style="padding:6px 10px;font-size:11px;font-weight:400;color:#748494;text-align:left;">'  + lbl("VIS_Invoice",     'Invoice')  + '</th>' +
                        '<th style="padding:6px 10px;font-size:11px;font-weight:400;color:#748494;text-align:right;">' + lbl("VIS_ColValue",    'Value')    + '</th>' +
                        '<th style="padding:6px 10px;font-size:11px;font-weight:400;color:#748494;text-align:right;">' + lbl("VIS_ColPending",  'Pending')  + '</th>' +
                    '</tr>' +
                '</thead>'
            );

            $tbody = $('<tbody><tr><td colspan="7" style="text-align:center;padding:16px;color:#748494;font-size:12px;">' + lbl("VIS_Loading", 'Loading…') + '</td></tr></tbody>');

            $table.append($thead).append($tbody);
            $scroll.append($table);

            /* ── WHY pill + text ── */
            var $why = $('<div style="display:flex;align-items:flex-start;gap:6px;padding-top:10px;flex-shrink:0;">');

            var $pill = $(
                '<span style="' +
                    'display:inline-flex;align-items:center;' +
                    'background:oklch(0.96 0.03 220);' +
                    'padding:1px 7px;border-radius:999px;flex-shrink:0;margin-top:2px;' +
                    'font-size:9px;font-weight:700;letter-spacing:0.08em;' +
                    'color:oklch(0.45 0.15 220);font-family:Roboto,monospace;' +
                '">' + lbl("VIS_Why", 'WHY') + '</span>'
            );

            var $whyText = $(
                '<span style="font-size:11px;color:#748494;line-height:1.45;">' +
                    lbl("VIS_AgingWhyText", 'Older invoices are harder to collect. Focus on the 61+ buckets.') +
                '</span>'
            );

            $why.append($pill).append($whyText);

            $card.append($header).append($scroll).append($why);
            $root.append($card);
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {
            loadData();
        };

        /* ── Root accessor ── */
        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VIS.AgingReceivablesWidget.prototype.refreshWidget = function () {};

    VIS.AgingReceivablesWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.AgingReceivablesWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.AgingReceivablesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
