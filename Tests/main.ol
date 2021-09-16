int main() {
    // TODO Add the code to run the tests
    dir := opendir(".");
    if dir {
        file := readdir(dir);
        while file {
            printf("File type: %d, File name: %s\n", file.d_type, &file.d_name);

            file = readdir(dir);
        }

        closedir(dir);
    }
    return 0;
}

#run main();
