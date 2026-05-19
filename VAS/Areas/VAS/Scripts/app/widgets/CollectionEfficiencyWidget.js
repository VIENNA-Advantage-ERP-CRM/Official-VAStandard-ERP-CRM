/**
 * Collection Efficiency Widget
 * Purpose - Show collection efficiency, DSO and overdue AR invoices.
 *
 * ── Labels / Message Keys ─────────────────────────────────────────────
 *  #  | Current Text                 | Message Key
 * ----+------------------------------+--------------------------------
 *  1  | Collection efficiency        | VIS_CollectionEfficiency
 *  2  | DSO                          | VIS_DSO
 *  3  | LAST 30 DAYS                 | VIS_Last30Days
 *  4  | This Month                   | VIS_ThisMonth
 *  5  | Last Month                   | VIS_LastMonth
 *  6  | Last Quarter                 | VIS_LastQuarter
 *  7  | Custom Date                  | VIS_CustomDate
 *  8  | Review aging                 | VIS_ReviewAging
 *  9  | From Date                    | VIS_FromDate
 * 10  | To Date                      | VIS_ToDate
 * 11  | Clear                        | VIS_Clear
 * 12  | Apply                        | VIS_Apply
 * ─────────────────────────────────────────────────────────────────────
 */
; VIS = window.VIS || { }
;

; (function(VIS, $) {

    VIS.CollectionEfficiencyWidget = function() {

        this.frame;
        this.windowNo;

        var $root = $('<div class="vas-ce-root">');
        var $percentText;
        var $dsoText;
        var $overdueText;
        var $filterText;
        var $customPanel;
        var $fromDate;
        var $toDate;

        var selectedFilter = "ThisMonth";

        function lbl(key, fallback)
        {
            var t = VIS.Msg.getMsg(key);
            return (t && t.charAt(0) !== '[') ? t : fallback;
        }

        this.Initalize = function() {
            createWidget();
            loadData();
        }
        ;

        function loadData()
        {
            setLoading();

            var requestData = {
                filterType: selectedFilter
            }
    ;

    if (selectedFilter === "Custom")
    {
        requestData.fromDate = $fromDate ? $fromDate.val() : "";
        requestData.toDate = $toDate ? $toDate.val() : "";
    }

            $.ajax({
    url: VIS.Application.contextUrl + 'CollectionEfficiency/GetCollectionEfficiency',
                type: 'GET',
                data: requestData,
                success: function(res) {
            var data = res;

            if (typeof data === 'string')
            {
                data = JSON.parse(data);
            }

            if (typeof data === 'string')
            {
                data = JSON.parse(data);
            }

            if (data && data.error)
            {
                setNoData();
                return;
            }

            renderData(data);
        },
                error: function() {
            setNoData();
        }
    });
}

function setLoading()
{
    if ($percentText) {
                $percentText.text("...");
    }

    if ($dsoText) {
                $dsoText.text(lbl("VIS_Loading", "Loading…"));
    }

    if ($overdueText) {
                $overdueText.text("");
    }
}

function setNoData()
{
    if ($percentText) {
                $percentText.text("0%");
    }
    
    if ($root) {
        $root.find('.vas-ce-ring').css('background', 'conic-gradient(#529b88 0deg 0deg, #f1f5f9 0deg 360deg)');
    }

    if ($dsoText) {
                $dsoText.text("DSO 0 days · target 22");
    }

    if ($overdueText) {
                $overdueText.text("0.00 overdue across 0 invoices");
    }
}

function formatCompactAmount(value)
{
    value = Number(value || 0);

    if (value >= 10000000)
    {
        return (value / 10000000).toFixed(2).replace(/\.00$/, "") + "Cr";
    }

    if (value >= 100000)
    {
        return (value / 100000).toFixed(2).replace(/\.00$/, "") + "L";
    }

    if (value >= 1000)
    {
        return (value / 1000).toFixed(2).replace(/\.00$/, "") + "K";
    }

    return value.toLocaleString(window.navigator.language, {
    minimumFractionDigits: 2,
                maximumFractionDigits: 2
            });
}

function getFilterLabel()
{
    if (selectedFilter === "LastMonth")
    {
        return lbl("VIS_LastMonth", "Last Month");
    }

    if (selectedFilter === "LastQuarter")
    {
        return lbl("VIS_LastQuarter", "Last Quarter");
    }

    if (selectedFilter === "Custom")
    {
        return lbl("VIS_CustomDate", "Custom Date");
    }

    return lbl("VIS_ThisMonth", "This Month");
}

function renderData(data)
{
    var percent = Number(data.collectionEfficiencyPercent || 0);
    var dsoDays = Number(data.dsoDays || 0);
    var targetDays = Number(data.dsoTargetDays || 22);
    var overdueAmount = Number(data.overdueAmount || 0);
    var overdueInvoiceCount = Number(data.overdueInvoiceCount || 0);

    if ($percentText) {
                $percentText.text(percent.toFixed(0) + "%");
    }
    
    if ($root) {
        var deg = (percent * 3.6).toFixed(0);
        $root.find('.vas-ce-ring').css('background', 'conic-gradient(#529b88 0deg ' + deg + 'deg, #f1f5f9 ' + deg + 'deg 360deg)');
    }

    if ($dsoText) {
                $dsoText.html("<strong>DSO</strong> " + dsoDays + " days · target " + targetDays);
    }

    if ($overdueText) {
                $overdueText.text(formatCompactAmount(overdueAmount) + " overdue across " + overdueInvoiceCount + " invoices");
    }

    if ($filterText) {
                $filterText.text(getFilterLabel());
    }
}

function createWidget()
{
    var $card = $(
        '<div class="vas-ce-card">' +
            '<div class="vas-ce-topbar">' +
                '<div class="vas-ce-header">' +
                    '<div class="vas-ce-icon-wrap">' +
                        '<svg width="25" height="25" viewBox="0 0 24 24" fill="none" ' +
                            'stroke="#4F8EEA" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">' +
                            '<circle cx="12" cy="12" r="9"/>' +
                            '<path d="M12 7v5l4 2"/>' +
                        '</svg>' +
                    '</div>' +
                    '<div>' +
                        '<div class="vas-ce-title">' +
                            lbl("VIS_CollectionEfficiency", "Collection efficiency") +
                        '</div>' +
                        '<div class="vas-ce-subtitle">' +
                            lbl("VIS_DSO", "DSO") +
                        '</div>' +
                    '</div>' +
                '</div>' +

                '<div class="vas-ce-filter-wrap">' +
                    '<button type="button" class="vas-ce-filter-btn">' +
                        '<span class="vas-ce-filter-text">' +
                            lbl("VIS_ThisMonth", "This Month") +
                        '</span>' +
                        '<span class="vas-ce-filter-arrow">▾</span>' +
                    '</button>' +
                    '<div class="vas-ce-filter-menu">' +
                        '<div class="vas-ce-filter-item" data-filter="ThisMonth">' + lbl("VIS_ThisMonth", "This Month") + '</div>' +
                        '<div class="vas-ce-filter-item" data-filter="LastMonth">' + lbl("VIS_LastMonth", "Last Month") + '</div>' +
                        '<div class="vas-ce-filter-item" data-filter="LastQuarter">' + lbl("VIS_LastQuarter", "Last Quarter") + '</div>' +
                        '<div class="vas-ce-filter-item" data-filter="Custom">' + lbl("VIS_CustomDate", "Custom Date") + '</div>' +
                    '</div>' +
                '</div>' +
            '</div>' +

            '<div class="vas-ce-custom-panel">' +
                '<div class="vas-ce-custom-row">' +
                    '<label>' + lbl("VIS_FromDate", "From Date") + '</label>' +
                    '<input type="date" class="vas-ce-from-date" />' +
                '</div>' +
                '<div class="vas-ce-custom-row">' +
                    '<label>' + lbl("VIS_ToDate", "To Date") + '</label>' +
                    '<input type="date" class="vas-ce-to-date" />' +
                '</div>' +
                '<div class="vas-ce-custom-actions">' +
                    '<button type="button" class="vas-ce-clear-btn">' + lbl("VIS_Clear", "Clear") + '</button>' +
                    '<button type="button" class="vas-ce-apply-btn">' + lbl("VIS_Apply", "Apply") + '</button>' +
                '</div>' +
            '</div>' +

            '<div class="vas-ce-content">' +
                '<div class="vas-ce-ring-wrap">' +
                    '<div class="vas-ce-ring">' +
                        '<div class="vas-ce-ring-inner">' +
                            '<div class="vas-ce-percent">...</div>' +
                            '<div class="vas-ce-on-time">ON-TIME</div>' +
                        '</div>' +
                    '</div>' +
                '</div>' +

                '<div class="vas-ce-details">' +
                    '<div class="vas-ce-dso"><strong>DSO</strong> 0 days · target 22</div>' +
                    '<div class="vas-ce-overdue">0.00 overdue across 0 invoices</div>' +
                    '<button type="button" class="vas-ce-review-btn">' +
                        lbl("VIS_ReviewAging", "Review aging") +
                        '<span>↗</span>' +
                    '</button>' +
                '</div>' +
            '</div>' +
        '</div>'
    );

            $percentText = $card.find('.vas-ce-percent');
            $dsoText = $card.find('.vas-ce-dso');
            $overdueText = $card.find('.vas-ce-overdue');
            $filterText = $card.find('.vas-ce-filter-text');
            $customPanel = $card.find('.vas-ce-custom-panel');
            $fromDate = $card.find('.vas-ce-from-date');
            $toDate = $card.find('.vas-ce-to-date');

            $card.find('.vas-ce-filter-btn').on('click', function() {
                $card.find('.vas-ce-filter-menu').toggleClass('vas-ce-filter-menu-show');
    });

            $card.find('.vas-ce-filter-item').on('click', function() {
        selectedFilter = $(this).data('filter');
                $card.find('.vas-ce-filter-menu').removeClass('vas-ce-filter-menu-show');

        if ($filterText) {
                    $filterText.text(getFilterLabel());
        }

        if (selectedFilter === "Custom")
        {
                    $customPanel.addClass('vas-ce-custom-panel-show');
            return;
        }

                $customPanel.removeClass('vas-ce-custom-panel-show');
        loadData();
    });

            $card.find('.vas-ce-clear-btn').on('click', function() {
                $fromDate.val("");
                $toDate.val("");
    });

            $card.find('.vas-ce-apply-btn').on('click', function() {
        if (!$fromDate.val() || !$toDate.val()) {
            return;
        }

        selectedFilter = "Custom";

        if ($filterText) {
                    $filterText.text(getFilterLabel());
        }

                $customPanel.removeClass('vas-ce-custom-panel-show');
        loadData();
    });

            $root.append($card);
}

this.refreshWidget = function() {
    loadData();
}
;

this.getRoot = function() {
    return $root;
}
;

this.disposeComponent = function() {
            $root.remove();
}
;
    };

VIS.CollectionEfficiencyWidget.prototype.init = function(windowNo, frame) {
    this.frame = frame;
    this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
    this.windowNo = windowNo;
    this.Initalize();
    this.frame.getContentGrid().append(this.getRoot());
}
;

VIS.CollectionEfficiencyWidget.prototype.widgetSizeChange = function(height, width) {
}
;

VIS.CollectionEfficiencyWidget.prototype.refreshWidget = function() {
    this.refreshWidget();
}
;

VIS.CollectionEfficiencyWidget.prototype.dispose = function() {
    this.disposeComponent();

    if (this.frame)
    {
        this.frame.dispose();
    }

    this.frame = null;
}
;

})(VIS, jQuery);