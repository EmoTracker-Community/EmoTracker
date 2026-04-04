SET SENTRY_AUTH_TOKEN=aebe14e66805445a971773c97b8e1fc254d696e251654ca88ab0cf79a431bd5b
SET SENTRY_ORG=emotracker

sentry-cli releases new -p emotracker %1
sentry-cli releases set-commits --auto %1