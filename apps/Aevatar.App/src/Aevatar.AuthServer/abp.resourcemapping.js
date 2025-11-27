module.exports = {
    aliases: {
        "@node_modules": "./node_modules",
        "@libs": "./wwwroot/libs"
    },
    mappings: {
        "@node_modules/@abp/aspnetcore.mvc.ui.theme.leptonxlite/**/*": "@libs/",
        "@node_modules/datatables.net/js/**/*.js": "@libs/datatables.net/js/",
        "@node_modules/datatables.net-bs5/js/**/*.js": "@libs/datatables.net-bs5/js/",
        "@node_modules/datatables.net-bs5/css/**/*.css": "@libs/datatables.net-bs5/css/"
    }
};

