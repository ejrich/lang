#include <stdio.h>
#include <dirent.h>

struct A {int a; int b[];};

A get_A() {
    A a;
    a.a = 84;
    return a;
}

int main() {
     DIR *d;
      struct dirent *dir;
      d = opendir(".");
      if (d) {
        while ((dir = readdir(d)) != NULL) {
          printf("%s\n", dir->d_name);
        }
        closedir(d);
      }

      return 0;
    /* int a = get_A().b; */
    /* printf("Value = %d\n", a); */
}
