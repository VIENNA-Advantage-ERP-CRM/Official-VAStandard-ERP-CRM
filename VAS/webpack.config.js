// This script is responsible for configuring webpack for building and bundling assets.
// It sets up rules for handling JavaScript and CSS files, optimization for minimizing files,
// and plugins for extracting CSS into separate files.

// NOTE: Please replace 'ViennaAdvantage' with the prefix of your module.

const path = require('path');
const MiniCssExtractPlugin = require('mini-css-extract-plugin');
const CssMinimizerPlugin = require('css-minimizer-webpack-plugin');
const TerserPlugin = require('terser-webpack-plugin');
const { CleanWebpackPlugin } = require('clean-webpack-plugin');
const fs = require('fs');

// Function to delete files by pattern from a directory
const deleteFilesByPattern = (directory, pattern) => {
    const files = fs.readdirSync(directory);
    const regex = new RegExp(pattern);
    files.forEach(file => {
        if (regex.test(file)) {
            fs.unlinkSync(path.join(directory, file));
        }
    });
};


// Delete CSS files with specific patterns
 deleteFilesByPattern(path.resolve(__dirname, 'Areas/VAS/Content'), /^VAS.all\.min\d+\.\d+\.\d+\.\d+\.js$/);



// Define versions for different asset types
const versions = {
    'VAS.all': '1.4.2.3', // JS Version
    //'ViennaAdvantageareactjs': '1.0.0.0', // React version
    'VAS': '1.4.2.3' // CSS version
};

module.exports = {
    mode: 'development', // for debug
    //mode: 'production', // for minify
    // Entry points for different assets
    entry: {
        //'ViennaAdvantageJs': './Areas/ViennaAdvantage/Scripts/src/ViennaAdvantageJs.js',
        'VAS.all': './Areas/VAS/Scripts/src/VASjs.js',
        'VAS': './Areas/VAS/Content/src/VASstyle.css'
    },
    output: {
        // Output file names with versioning
        filename: ({ chunk }) => {
            const name = chunk.name;
            const version = versions[name] || '1.0.0.0'; // Default version if not specified
            return `${name}.min${version}.js`;
        },
        path: path.resolve(__dirname, 'Areas/VAS/Scripts/dist')
    },
    resolve: {
        extensions: ['.jsx', '.js'],
    },
    module: {
        rules: [
            {
                // Rule for handling JavaScript/JSX files
                test: /\.(js|jsx)$/,
                exclude: /node_modules/,
                use: {
                    loader: 'babel-loader'
                },
            },
            {
                // Rule for handling CSS/Sass files
                test: /\.(css|sass)$/,
                use: [MiniCssExtractPlugin.loader, {
                    loader: 'css-loader',
                    options: {
                        url: false,
                    }
                },
                {
                    loader: 'sass-loader',
                    options: {
                        sassOptions: {
                            url: false
                        }
                    }
                }
                ],
            }
        ]
    },
    plugins: [
        new CleanWebpackPlugin(),
        // Plugin for extracting CSS into separate files
        new MiniCssExtractPlugin({
            filename: ({ chunk }) => {
                const name = chunk.name;
                const version = versions[name] || '1.0.0.0'; // Default version if not specified
                return `../../Content/${name}.all.min${version}.css`;
            },
        }),
    ],
    optimization: {
        minimize: true,
        minimizer: [
            new CssMinimizerPlugin(), // Minify CSS
            new TerserPlugin({
                terserOptions: {
                    format: {
                        comments: false,
                    },
                },
                extractComments: false,
            }), // Minify JavaScript
        ]
    },
};
