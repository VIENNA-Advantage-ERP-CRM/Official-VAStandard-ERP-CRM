const path = require('path');
const MiniCssExtractPlugin = require('mini-css-extract-plugin');
const CssMinimizerPlugin = require('css-minimizer-webpack-plugin');
const TerserPlugin = require('terser-webpack-plugin');
const { CleanWebpackPlugin } = require('clean-webpack-plugin');
const fs = require('fs');

const deleteFilesByPattern = (directory, pattern) => {
    const files = fs.readdirSync(directory);
    const regex = new RegExp(pattern);
    files.forEach(file => {
        if (regex.test(file)) {
            fs.unlinkSync(path.join(directory, file));
        }
    });
};

deleteFilesByPattern(path.resolve(__dirname, 'Areas/VAS/Content'), /^VAS\.all\.min\.css$/);


const versions = {
    'VAS.all': '1.6.9.0',    
    'VAS': '1.6.9.0' // CSS Version
};

module.exports = {
    //mode: 'development', // for debuggin
    mode: 'production',
    entry: {
        'VAS.all': './Areas/VAS/Scripts/src/VASjs.js',        
        'VAS': './Areas/VAS/Content/src/VAScss.css'
    },
    output: {
        filename: ({ chunk }) => {
            const name = chunk.name;
            const version = versions[name] || '1.0.0.0'; // Default version if not specified
            return `${name}.min.js`;
        },
        path: path.resolve(__dirname, 'Areas/VAS/Scripts/dist')
    },
    resolve: {
        extensions: ['.jsx', '.js'],
    },
    module: {
        rules: [
            {
                test: /\.(js|jsx)$/,
                exclude: /node_modules/,
                use: {
                    loader: 'babel-loader'
                },
            },
            {
                test: /\.(css|sass)$/,
                use: [MiniCssExtractPlugin.loader, {
                    loader: 'css-loader',
                    options: {
                        url: false,
                    }
                }
                ],
            }
        ]
    },
    plugins: [
        new CleanWebpackPlugin(),
        new MiniCssExtractPlugin({
            filename: ({ chunk }) => {
                const name = chunk.name;
                const version = versions[name] || '1.0.0'; // Default version if not specified
                return `../../Content/${name}.all.min.css`;
            },
        }),
    ],
    optimization: {
        minimize: true, // Webpack 5 uses "minimize" instead of "minimizer"
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
