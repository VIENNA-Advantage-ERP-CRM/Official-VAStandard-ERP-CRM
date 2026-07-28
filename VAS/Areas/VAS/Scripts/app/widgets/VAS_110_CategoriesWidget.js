/**
 * Categories Widget (KPI Card)
 * Widget number 110 - reassign on hand-off.
 * Shows the distinct count of active product categories; meta shows the
 * distinct parent-category count ("product families", per the dev =
 * M_Product_Category_Parent_ID).
 * Backend - VAS_110_CategoriesWidget/GetCategories
 * Summary Message Table
 *  # | Current Text       | Message Key
 * ---+--------------------+------------------------------
 *  1 | Categories         | VAS_110_Categories
 *  2 | product families   | VAS_110_ProductFamilies
 *  3 | Couldn't load      | VAS_CouldntLoad
 */
; VAS = window.VAS || {};

; (function (VAS, $) {

    VAS.VAS_110_CategoriesWidget = function () {

        this.frame;
        this.windowNo;

        var $root = $('<div class="MPC-categories-root">');
        var $value;
        var $meta;
        var request;

        function label(key, fallback) {
            var translated = VIS.Msg.getMsg(key);
            return translated && translated.charAt(0) !== '[' ? translated : fallback;
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
                        $value.text(formatCount(result.category_count));
                        $meta.text(formatCount(result.family_count) + ' ' + label('VAS_110_ProductFamilies', 'product families'));
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
            $value.text('—');
            $meta.text(label('VAS_CouldntLoad', "Couldn't load"));
        }

        this.Initalize = function () {
            var $card = $(
                '<div class="MPC-categories-card" aria-live="polite">' +
                    '<div class="MPC-categories-label"></div>' +
                    '<div class="MPC-categories-value">—</div>' +
                    '<div class="MPC-categories-meta"></div>' +
                '</div>'
            );

            $card.find('.MPC-categories-label').text(label('VAS_110_Categories', 'Categories'));
            $value = $card.find('.MPC-categories-value');
            $meta = $card.find('.MPC-categories-meta');
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
