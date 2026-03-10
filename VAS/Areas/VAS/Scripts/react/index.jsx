import React from 'react';
import ReactDOM from 'react-dom/client';

//VAS Replace with module prefix

// Ensure VAS namespace and dependencies are available
window.VAS = window.VAS || {};
(function (VAS, $) {
    // Ensure VAS.React namespace is initialized
    VAS.React = VAS.React || {};

    // Constructor function for VAS.React
    VAS.React = function () {
        this.listener = null;
    };

    // Initialize method to render React component dynamically
    VAS.React.prototype.init = function (windowNo, frame, additionalInfo) {
        this.windowNo = windowNo;
        this.frame = frame;
        this.additionalInfo = additionalInfo;
        var componentName = frame.componentName;
        let self = this;
        // Lazy load the component based on componentName
        const MyComponent = React.lazy(() => import(`./pages/${componentName}`));

        // Render the component with Suspense for fallback
        ReactDOM.createRoot(frame.getContentGrid()[0]).render(
            <React.Suspense fallback={<div>Loading...</div>}>
                <MyComponent self={self} />
            </React.Suspense>
        );
    };

    VAS.React.prototype.widgetFirevalueChanged = function (value) {
        // this.getRoot().trigger('widgetFirevalueChanged', value); // Trigger custom event with the value
        if (this.listener)
            this.listener.widgetFirevalueChanged(value);
    };

    VAS.React.prototype.addChangeListener = function (listener) {
        this.listener = listener;
    }

})(VAS, jQuery);