FROM python:3.10-bullseye
RUN apt-get update

ENV PYTHONFAULTHANDLER=1 \
  PYTHONUNBUFFERED=1 \
  PYTHONHASHSEED=random \
  PIP_NO_CACHE_DIR=off \
  PIP_DISABLE_PIP_VERSION_CHECK=on \
  PIP_DEFAULT_TIMEOUT=100 \
  POETRY_VERSION=1.2.2

RUN pip install "poetry==$POETRY_VERSION"

WORKDIR /luna
COPY ./poetry.lock /luna
COPY ./pyproject.toml /luna
RUN poetry config virtualenvs.create false && poetry install --without dev --no-interaction --no-ansi

COPY ./src /luna/src

CMD ["python", "/luna/src/discord_bot.py"]