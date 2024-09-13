/************************************************************
 * Module Name    : VAS
 * Purpose        : Created Widget to get top 10 Lowest selling
 *                  Product
 * chronological  : Development
 * Created Date   : 09 Sep 2024
 * Created by     : VAI050
 ***********************************************************/
; VAS = window.VAS || {};
; (function (VAS, $) {

    VAS.VAS_LowestPrMonthlySalesWidget = function () {
        this.frame;
        this.windowNo;
        this.widgetInfo;
        var $bsyDiv;
        var $self = this;
        var ctx = this.ctx;
        var $root = $('<div class="h-100 w-100">'); // Root container
        var orgControlDiv;
        var unit = null;
        var sqlWHERE = "";
        var OrganizationUnit = 0;
        var stdPrecision = 0;
        var symbol = "";
        var $vOrg;

        function createStructure(widgetID) {
            var $contentContainer = $('<div class="VAS-org-lowest-unitsales-col VAS-content-container vis-formouterwrpdiv">' +
                '<div class="VAS-sales-heading">' + VIS.Msg.getMsg("VAS_LowestSellingProduct") + '</div>' +
                '<div class="VAS-organization-block">' +
                '<div id="VAS_Org_' + widgetID + '" class="VAS_OrgUnitControl"></div>' +
                '<div class="VAS-pagination-container"></div>' +
                '</div>' +
                '<div class="VAS-graph-details">' +
                '<div class="VAS-salesGraph-heading"><span id="VAS_ProductRank_' + widgetID + '">#1</span>&nbsp<span id="VAS_ProductName_' + widgetID + '"> Product Name</span></div>' +
                '<div class="VAS-totalSales-col">' +
                '<div class="VAS-startSale-box">' +
                '<div class="VAS-yearTxt"><span>2023<span></div>' +
                '<div class="VAS-totalSale"><span id="VAS_LastYearSales_' + widgetID + '">520 €</span></div>' +
                '</div>' +
                '<div class="VAS-endSale-box">' +
                '<div class="VAS-yearSale-col">' +
                '<div class="VAS-yearTxt"><span>2024<span></div>' +
                '<div class="VAS-totalSale"><span id="VAS_CurrentSales_' + widgetID + '">520 €</span></div>' +
                '</div>' +
                '<div class="VAS-graph-result">' +
                '<span class="vis vis-trending-down"></span>' +
                '<div class="repfix-resultTxt">-10.32%</div>' +
                '</div>' +
                '</div>' +
                '</div>' +
                '<div class="VAS-graph-col VAS-chart-container" style="display: flex; align-items: center; justify-content: center;">' +
                '</div>' +
                /*           '<div class="VAS-pagination-container"></div>' +*/
                '</div>');

            orgControlDiv = $contentContainer.find('#VAS_Org_' + widgetID);
            return $contentContainer;
        }

        //This function is used to create the VIS controls
        function createControls() {
            /* VIS control for Organization (Table) */
            orgControlDiv.empty();
             orgDivInputWrap = $('<div class="input-group vis-input-wrap">');
            $lookupOrg = VIS.MLookupFactory.get(VIS.Env.getCtx(), $self.windowNo, 0, VIS.DisplayType.Search, "AD_Org_ID", 0, false,sqlWHERE);
            $vOrg = new VIS.Controls.VTextBoxButton("AD_Org_ID", false, false, true, VIS.DisplayType.Search, $lookupOrg, 150);

            //$orgButtonWrap = $('<div class="input-group-append">');
            $orgControlWrap = $('<div class="vis-control-wrap">');
            orgDivInputWrap.append($orgControlWrap);
            $orgControlWrap.append($vOrg.getControl().attr('placeholder', ' ').attr('data-placeholder', '').attr('data-hasbtn', ' ')).append('<label>' + VIS.Msg.getMsg("VAS_OrgUnit") + '</label><span class= "vis-ev-ctrlinfowrap"</span>');

            //$orgButtonWrap.append($vOrg.getBtn(0));
            //orgDivInputWrap.append($orgButtonWrap);
            orgControlDiv.append(orgDivInputWrap);

            $vOrg.fireValueChanged = function () {
                OrganizationUnit = $vOrg.getValue() == null ? 0 : $vOrg.getValue();
                $self.intialLoad();
            }
        }
        var productList = [];
        var chartInstance;
        this.currentPage = 1;
        this.totalPages = 0;
        var widgetID = 0;



        this.initalize = function () {
            widgetID = this.widgetInfo.AD_UserHomeWidgetID;
            // Create busy indicator
            createBusyIndicator();
            var demo = createStructure(widgetID);
            $root.append(demo);
        };

        /* This function will load data in widget */
        this.intialLoad = function () {
            // Show busy indicator
            //$bsyDiv.css('visibility', 'visible'); 

            // AJAX request to fetch product list and sales data
            $.ajax({
                url: VIS.Application.contextUrl + "Product/GetProductSalesAndDetails",
                data: { OrganizationUnit: OrganizationUnit, Type: "ASC" },
                dataType: 'json',
                success: function (response) {
                    var response = JSON.parse(response);
                    if (response) {
                        productList = response.products; // Store the list of products
                        $self.totalPages = productList.length; // Update totalPages
                        symbol = response.Symbol;
                        stdPrecision = response.StdPrecision;
                        // Build pagination controls
                        buildPagination();
                        // Use the monthly sales data for the first product if available
                        if (productList.length > 0) {
                            var firstProduct = productList[0];
                            var monthlyData = response.product_monthly_sales;
                            loadChartData(monthlyData); // Load the chart data for the first product
                        }
                        sqlWHERE = response.sqlWhereForLookup;
                        createControls();
                        updatePagination();
                        $root.find('.VAS-graph-details').show(); // Clear the graph details container
                        $root.find('.VAS-pagination-container').show(); // Clear the pagination container
                        // Hide busy indicator
                        $bsyDiv[0].style.visibility = "hidden";
                    }
                    else {
                        productList = [];
                        $root.find('.VAS-graph-details').hide(); // Clear the graph details container
                        $root.find('.VAS-pagination-container').hide(); // Clear the pagination container
                        $bsyDiv[0].style.visibility = "hidden";
                    }
                },
                error: function (xhr, status, error) {
                    // Handle errors
                    console.error('Failed to fetch data:', status, error);
                    $bsyDiv[0].style.visibility = "hidden";
                }
            });
        };

        /* This function is used to create the busy indicator */
        function createBusyIndicator() {
            $bsyDiv = $('<div class="vis-busyindicatorouterwrap"><div class="vis-busyindicatorinnerwrap"><i class="vis_widgetloader"></i></div></div>');
            $root.append($bsyDiv);
        }

        /* This function builds pagination controls */
        function buildPagination() {
            var $paginationContainer = $root.find('.VAS-pagination-container');
            $paginationContainer.empty(); // Clear existing pagination
            if (productList.length > 0) {
                $self.totalPages = productList.length; // Update totalPages
                var $pagination = $('<div class="VAS-pagination-col">' +
                    '<div class="VAS-arrow-col">' +
                    '<a href="#" id="VAS_Prev_Page_' + widgetID + '"><span class="fa fa-arrow-left"></span></a>' +
                    '<a href="#" id="VAS_Next_Page_' + widgetID + '"><span class="fa fa-arrow-right"></span></a>' +
                    '</div>' +
                    '<div class="VAS-page-count"><span id="VAS_PageCount_' + widgetID + '">1 of ' + $self.totalPages + '</span></div>' +
                    '</div>');

                // Add event listeners for arrows
                $pagination.find('#VAS_Prev_Page_' + widgetID + '').on('click', function (e) {
                    e.preventDefault();
                    if ($self.currentPage > 1) {
                        $self.currentPage--;
                        loadProductData(productList[$self.currentPage - 1].Product_ID);
                    }
                });

                $pagination.find('#VAS_Next_Page_' + widgetID + '').on('click', function (e) {
                    e.preventDefault();
                    if ($self.currentPage < $self.totalPages) {
                        $self.currentPage++;
                        loadProductData(productList[$self.currentPage - 1].Product_ID);
                    }
                });

                // Append the pagination controls to the container
                $paginationContainer.append($pagination);

            }
        }

        /* This function updates the pagination controls display */
        function updatePagination() {
            var $pageCount = $root.find('#VAS_PageCount_' + widgetID);
            $pageCount.text($self.currentPage + ' of ' + $self.totalPages);
            var currentProduct = productList[$self.currentPage - 1];
            var lastYearSales = parseFloat(currentProduct.PreviousTotal);
            var currentSales = parseFloat(currentProduct.CurrentTotal);
            var percentageDifference = calculatePercentageDifference(currentSales, lastYearSales);

            $root.find('#VAS_ProductRank_' + widgetID).text('#' + $self.currentPage);
            $root.find('#VAS_ProductName_' + widgetID).text(currentProduct.ProductName);
            $root.find('#VAS_LastYearSales_' + widgetID).text(symbol + ' ' + formatLargeNumber(lastYearSales) + ' ' + unit);
            $root.find('#VAS_CurrentSales_' + widgetID).text(symbol + ' ' + + formatLargeNumber(currentSales) + ' ' + unit);
            $root.find('.repfix-resultTxt').text(percentageDifference);

            // Adjust the result icon based on the percentage difference
            var resultIcon = $root.find('.VAS-graph-result .vis');
            if (percentageDifference.startsWith('-')) {
                resultIcon.removeClass('vis-trending-up').addClass('vis-trending-down');
            } else {
                resultIcon.removeClass('vis-trending-down').addClass('vis-trending-up');
            }
            if (OrganizationUnit > 0) {
                $vOrg.setValue(OrganizationUnit);
            }
        }
        /* This function loads data for a given product page */
        function loadProductData(productId) {
            // AJAX request to fetch monthly sales data for the selected product
            $.ajax({
                url: VIS.Application.contextUrl + "Product/GetProductMonthlySalesData",
                data: { ProductID: productId, OrganizationUnit: OrganizationUnit },
                dataType: 'json',
                success: function (response) {
                    var monthlyData = JSON.parse(response);
                    loadChartData(monthlyData); // Load the chart data
                    updatePagination();
                },
                error: function (xhr, status, error) {
                    // Handle errors
                    console.error('Failed to fetch product data:', status, error);
                }
            });
        }

        function calculatePercentageDifference(currentSales, lastYearSales) {
            if (lastYearSales === 0) {
                return currentSales > 0 ? '100%' : '0%'; // Handle edge case of last year's sales being zero
            }
            var difference = currentSales - lastYearSales;
            var percentage = (difference / lastYearSales) * 100;
            return percentage.toFixed(2) + '%';
        }

        /**
          * This Function is responsible converting the value into million
          * @param {any} number
          */
        function formatLargeNumber(number) {
            if (number >= 1000000000000) { /* Trillion*/
                unit = VIS.Msg.getMsg("VAS_Trillion");
                return (number / 1000000000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            } else if (number >= 1000000000) { /* Billion*/
                unit = VIS.Msg.getMsg("VAS_Billion");
                return (number / 1000000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            } else if (number >= 1000000) { /* Million*/
                unit = VIS.Msg.getMsg("VAS_Million");
                return (number / 1000000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
            else if (number >= 1000) { /* Thousand*/
                unit = VIS.Msg.getMsg("VAS_Thousand");
                return (number / 1000).toLocaleString(window.navigator.language, { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            }
            else {
                unit = "";
                return (number).toLocaleString(window.navigator.language, { minimumFractionDigits: stdPrecision, maximumFractionDigits: stdPrecision });
            }
        }

        /* This function loads chart data */
        function loadChartData(monthlyData) {
            const labels = monthlyData.map(item => item.month);
            const cyData = monthlyData.map(item => item.cy_month_sales);
            const lyData = monthlyData.map(item => item.ly_month_sales);
            // Define chart data and configuration
            const data = {
                labels: labels,
                datasets: [
                    {
                        label: VIS.Msg.getMsg("VAS_CurrentYearSales"),
                        data: cyData,
                        fill: false,
                        borderColor: 'rgb(75, 192, 192)',
                        tension: 0.1,
                        datalabels: {
                            //color: 'rgb(75, 192, 192)',
                            backgroundColor: 'rgba(75, 192, 192, 0.2)',
                            borderRadius: 3,
                            padding: 6,
                            font: {
                                weight: 'bold',
                                size: 12
                            },
                            align: 'center',
                            anchor: 'end'
                        }
                    },
                    {
                        label: VIS.Msg.getMsg("VAS_LastYearSales"),
                        data: lyData,
                        fill: false,
                        borderColor: 'rgb(255, 99, 132)',
                        tension: 0.1,
                        datalabels: {
                            //color: 'rgb(255, 99, 132)',
                            backgroundColor: 'rgba(255, 99, 132, 0.2)',
                            borderRadius: 3,
                            padding: 6,
                            font: {
                                weight: 'bold',
                                size: 12
                            },
                            align: 'center',
                            anchor: 'end'
                        }
                    }
                ]
            };

            const config = {
                type: 'line',
                data: data,
                options: {
                    responsive: true,
                    maintainAspectRatio: false,
                    plugins: {
                        legend: {
                            position: 'bottom' // Position the legend at the bottom
                        },
                        datalabels: {
                            display: true
                        }
                    }
                },
                plugins: [ChartDataLabels] // Add the data labels plugin here
            };

            if (chartInstance) {
                chartInstance.data = data;
                chartInstance.update();
            } else {
                // Remove existing canvas if exists
                $root.find('canvas').remove();

                // Create a new canvas element and append it to the root
                const canvas = $('<canvas></canvas>');
                var chartContainer = $root.find('.VAS-chart-container');
                chartContainer.append(canvas);
                // Initialize the chart with the new data
                const ctx = canvas[0].getContext('2d');
                chartInstance = new Chart(ctx, config);
            }

        }


        this.getRoot = function () {
            return $root;
        };

        /* This function is used to refresh the widget data */
        this.refreshWidget = function () {
            chartInstance = null;
            $self.currentPage = 1;
            $self.totalPages = 0;
            $self.intialLoad();
        };
    };

    VAS.VAS_LowestPrMonthlySalesWidget.prototype.init = function (windowNo, frame) {
        this.frame = frame;
        this.widgetInfo = frame.widgetInfo;
        this.windowNo = windowNo;
        this.initalize();
        this.frame.getContentGrid().append(this.getRoot());
        var ssef = this;
        window.setTimeout(function () {
            ssef.intialLoad();
        }, 50);
    };

    VAS.VAS_LowestPrMonthlySalesWidget.prototype.widgetSizeChange = function (widget) {
        this.widgetInfo = widget;
    };

    VAS.VAS_LowestPrMonthlySalesWidget.prototype.refreshWidget = function () {
        this.refreshWidget();
    };

    VAS.VAS_LowestPrMonthlySalesWidget.prototype.dispose = function () {
        this.frame = null;
        this.windowNo = null;
        $bsyDiv = null;
        $self = null;
        $root = null;
        $contentContainer = null;
    };

})(VAS, jQuery);