/**
 * Sales Invoice Customer Search Widget
 * Purpose - Search bar for finding invoices by customer, number, amount or status
 */
; VIS = window.VIS || {};

; (function (VIS, $) {

    VIS.SalesInvoiceCustomerSearchWidget = function () {

        this.frame;
        this.windowNo;
        var $self = this;
        var $root = $('<div style="height:100%;font-family:Roboto,sans-serif;">');

        var $input;

        var SUGGESTIONS = [
            'Overdue invoices',
            'Over $5,000',
            'Unpaid this month',
            'Northwind Logistics',
            'Draft invoices'
        ];

        /* ── Initialize ── */
        this.Initalize = function () {
            createWidget();
            bindEvents();
        };

        /* ── Build DOM ── */
        function createWidget() {
            var $card = $(
                '<div style="' +
                    'background:#FFFFFF;' +
                    'border-radius:14px;' +
                    'padding:16px 18px;' +
                    'border:1px solid #E4EDF4;' +
                    'box-shadow:0 10px 24px rgba(15,61,97,0.06);' +
                '">'
            );

            /* Search row */
            var $searchRow = $(
                '<div style="display:flex;align-items:center;gap:12px;">'
            );

            /* Search icon well */
            var $iconWell = $(
                '<div style="' +
                    'width:36px;height:36px;border-radius:10px;' +
                    'background:#EAF8FF;' +
                    'display:flex;align-items:center;justify-content:center;' +
                    'flex-shrink:0;' +
                '">' +
                    '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" ' +
                        'stroke="#0083DA" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">' +
                        '<circle cx="11" cy="11" r="8"/>' +
                        '<path d="M21 21l-4.35-4.35"/>' +
                    '</svg>' +
                '</div>'
            );

            /* Input */
            $input = $(
                '<input type="text" ' +
                    'placeholder="Find invoices by customer, number, amount or status…" ' +
                    'style="' +
                        'flex:1 1 0%;border:none;outline:none;' +
                        'font-family:Roboto,sans-serif;font-size:14px;' +
                        'color:#102C3F;background:transparent;' +
                    '">'
            );

            /* Keyboard shortcut badge */
            var $kbd = $(
                '<span style="' +
                    'padding:3px 7px;font-size:11px;font-weight:600;' +
                    'color:#748494;background:#F3F6F9;' +
                    'border-radius:6px;border:1px solid #E4EDF4;' +
                    'letter-spacing:0.3px;flex-shrink:0;font-family:monospace;' +
                '">⌘K</span>'
            );

            /* Filters button */
            var $filtersBtn = $(
                '<button style="' +
                    'display:flex;align-items:center;gap:5px;' +
                    'padding:5px 10px;background:none;border:none;cursor:pointer;' +
                    'font-family:Roboto,sans-serif;font-size:13px;font-weight:600;' +
                    'color:#5F7283;border-radius:8px;flex-shrink:0;' +
                '">' +
                    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" ' +
                        'stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round">' +
                        '<polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/>' +
                    '</svg>' +
                    ' Filters' +
                '</button>'
            );

            $searchRow.append($iconWell).append($input).append($kbd).append($filtersBtn);

            /* Suggestions row */
            var $suggestRow = $(
                '<div style="display:flex;align-items:center;gap:6px;margin-top:12px;flex-wrap:wrap;">'
            );

            var $tryLabel = $(
                '<span style="' +
                    'font-size:10px;font-weight:600;color:#748494;' +
                    'letter-spacing:0.6px;text-transform:uppercase;' +
                    'margin-right:4px;flex-shrink:0;font-family:monospace;' +
                '">TRY:</span>'
            );
            $suggestRow.append($tryLabel);

            $.each(SUGGESTIONS, function (i, label) {
                var $chip = $(
                    '<button data-suggestion="' + label + '" style="' +
                        'font-size:12px;padding:4px 10px;border-radius:999px;' +
                        'background:#F3F6F9;border:1px solid #E4EDF4;' +
                        'color:#3D5166;cursor:pointer;font-family:Roboto,sans-serif;' +
                    '">' + label + '</button>'
                );
                $suggestRow.append($chip);
            });

            $card.append($searchRow).append($suggestRow);
            $root.append($card);
        }

        /* ── Events ── */
        function bindEvents() {

            /* Chip hover */
            $root.on('mouseenter', '[data-suggestion]', function () {
                $(this).css({ background: '#EAF8FF', borderColor: '#BFE4FF' });
            });
            $root.on('mouseleave', '[data-suggestion]', function () {
                $(this).css({ background: '#F3F6F9', borderColor: '#E4EDF4' });
            });

            /* Chip click — populate input */
            $root.on('click', '[data-suggestion]', function () {
                $input.val($(this).data('suggestion')).focus();
            });

            /* Filters button */
            $root.on('click', 'button:not([data-suggestion])', function () {
                // TODO: wire filter panel when backend is ready
            });
        }

        /* ── Refresh ── */
        this.refreshWidget = function () {};

        /* ── Root accessor ── */
        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            $root.remove();
        };
    };

    VIS.SalesInvoiceCustomerSearchWidget.prototype.refreshWidget = function () {};

    VIS.SalesInvoiceCustomerSearchWidget.prototype.init = function (windowNo, frame) {
        this.frame               = frame;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.windowNo            = windowNo;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VIS.SalesInvoiceCustomerSearchWidget.prototype.widgetSizeChange = function (height, width) {};

    VIS.SalesInvoiceCustomerSearchWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame)
            this.frame.dispose();
        this.frame = null;
    };

})(VIS, jQuery);
