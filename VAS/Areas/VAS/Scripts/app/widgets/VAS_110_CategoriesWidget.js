/**
 * Categories Widget (KPI Card)
 * Widget number 110 - reassign on hand-off.
 * Shows the distinct count of active product categories. The meta line used
 * to show the distinct parent-category count ("product families", per
 * M_Product_Category_Parent_ID) but was removed on explicit request: this
 * data has no category hierarchy configured, so the count was always 0 and
 * never conveyed anything to the user.
 * Backend - VAS_110_CategoriesWidget/GetCategories
 * Summary Message Table
 *  # | Current Text       | Message Key
 * ---+--------------------+------------------------------
 *  1 | Categories         | VAS_110_Categories
 *  2 | Couldn't load      | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_110_CategoriesWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-categories-root">');
        var $value;
        var request;

        function label(key, fallback) {
            return VIS.Msg.getMsg(key);
        }

        function formatCount(value) {
            return Number(value || 0).toLocaleString(window.navigator.language, { maximumFractionDigits: 0 });
        }

        function loadCategories() {
            if (request && request.readyState !== 4) { request.abort(); }
            $value.text('—');

            request = $.ajax({
                url: VIS.Application.contextUrl + 'VAS_110_CategoriesWidget/GetCategories',
                type: 'GET',
                cache: false,
                success: function (response) {
                    var result = response;
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }
                    if (typeof result === 'string' && result) { result = JSON.parse(result); }

                    if (result && !result.error) {
                        $value.removeAttr('title').text(formatCount(result.category_count));
                        return;
                    }

                    showError();
                },
                error: function (xhr, status) {
                    if (status !== 'abort') { showError(); }
                }
            });
        }

        function showError() {
            $value.text('—').attr('title', label('VAS_CouldntLoad', "Couldn't load"));
        }

        this.Initalize = function () {
            var $card = $(
                '<div class="MPC-categories-card" aria-live="polite">' +
                    '<div class="MPC-categories-label"></div>' +
                    '<div class="MPC-categories-value">—</div>' +
                '</div>'
            );

            $card.find('.MPC-categories-label').text(label('VAS_110_Categories', 'Categories'));
            $value = $card.find('.MPC-categories-value');
            $root.append($card);
            loadCategories();
        };

        this.refreshWidget = function () {
            loadCategories();
        };

        this.getRoot = function () {
            return $root;
        };

        this.disposeComponent = function () {
            if (request && request.readyState !== 4) { request.abort(); }
            $root.remove();
        };
    };

    VAS.VAS_110_CategoriesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.windowNo = windowNo;
        this.AD_UserHomeWidgetID = frame.widgetInfo.AD_UserHomeWidgetID;
        this.Initalize();
        this.frame.getContentGrid().append(this.getRoot());
    };

    VAS.VAS_110_CategoriesWidget.prototype.widgetSizeChange = function () { };

    VAS.VAS_110_CategoriesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_110_CategoriesWidget.prototype.dispose = function () {
        this.disposeComponent();
        if (this.frame) { this.frame.dispose(); }
        this.frame = null;
    };

})(VAS, jQuery);
