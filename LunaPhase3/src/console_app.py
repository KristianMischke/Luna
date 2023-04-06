from luna import Luna

luna = Luna(print)

while True:
    print("")
    user_input = input()
    luna.receive_message(user_input)
